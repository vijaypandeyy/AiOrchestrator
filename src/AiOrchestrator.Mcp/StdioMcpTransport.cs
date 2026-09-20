using System.Collections.Concurrent;
using System.Diagnostics;
using AiOrchestrator.Core.Models;
using AiOrchestrator.Mcp.Protocol;
using Microsoft.Extensions.Logging;

namespace AiOrchestrator.Mcp;

/// <summary>
/// Low-level duplex transport that launches an MCP server as a child process and exchanges
/// newline-delimited JSON-RPC messages with it over stdin/stdout, per the MCP stdio transport
/// spec. This class knows nothing about MCP *semantics* (initialize/tools/etc.) - it only
/// knows how to get a <see cref="JsonRpcMessage"/> to the process and correlate the response
/// that eventually comes back. <see cref="McpClient"/> builds the protocol semantics on top.
///
/// The child process's stderr stream is deliberately treated as free-form diagnostic log
/// output (never protocol), matching MCP convention: stdout is reserved exclusively for
/// JSON-RPC frames so an errant Console.WriteLine in a server never corrupts the stream.
/// </summary>
public sealed class StdioMcpTransport : IAsyncDisposable
{
    private readonly Process _process;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonRpcMessage>> _pending = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
    private Task? _stdoutLoop;
    private Task? _stderrLoop;
    private volatile bool _started;
    private volatile bool _faulted;

    public string ServerId { get; }

    /// <summary>
    /// Never throws, whatever state the child process is in. A server whose executable is missing
    /// never gets past <see cref="Start"/>, and <see cref="Process.HasExited"/> throws for a
    /// <see cref="Process"/> that was never successfully started - so callers such as
    /// <c>ToolCatalog</c> would otherwise see an exception instead of an unhealthy server.
    /// </summary>
    public bool IsHealthy => _started && !_faulted && !HasProcessExited();

    private bool HasProcessExited()
    {
        try
        {
            return _process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true; // No process is associated with this object - treat as gone.
        }
    }

    public StdioMcpTransport(McpServerConfig config, ILogger logger)
    {
        ServerId = config.Id;
        _logger = logger;

        var startInfo = new ProcessStartInfo
        {
            FileName = config.Command,
            WorkingDirectory = string.IsNullOrWhiteSpace(config.WorkingDirectory) ? null : config.WorkingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in config.Args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        foreach (var (key, value) in config.Environment)
        {
            startInfo.Environment[key] = value;
        }

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
    }

    public void Start()
    {
        bool started;
        try
        {
            started = _process.Start();
        }
        catch (Exception ex)
        {
            // A missing executable or bad working directory surfaces here (Win32Exception), not as
            // a false return value, so the flag has to be set on this path too.
            _faulted = true;
            throw new InvalidOperationException(
                $"Failed to start MCP server process for '{ServerId}' (command '{_process.StartInfo.FileName}').", ex);
        }

        if (!started)
        {
            _faulted = true;
            throw new InvalidOperationException($"Failed to start MCP server process for '{ServerId}'.");
        }

        _started = true;

        _process.Exited += (_, _) =>
        {
            _faulted = true;
            FailAllPending($"MCP server process '{ServerId}' exited unexpectedly (exit code {SafeExitCode()}).");
        };

        _stdoutLoop = Task.Run(ReadStdoutLoopAsync);
        _stderrLoop = Task.Run(ReadStderrLoopAsync);
    }

    private int SafeExitCode()
    {
        try
        {
            return _process.ExitCode;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// Registers a request awaiting its response, correlated by the id's raw JSON text (e.g. <c>7</c>
    /// or <c>"abc"</c>) rather than a number: JSON-RPC 2.0 permits string ids, and a server that uses
    /// them must not be treated as a protocol failure.
    /// </summary>
    public TaskCompletionSource<JsonRpcMessage> RegisterPending(string idKey)
    {
        var tcs = new TaskCompletionSource<JsonRpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[idKey] = tcs;
        return tcs;
    }

    public void CancelPending(string idKey)
    {
        if (_pending.TryRemove(idKey, out var tcs))
        {
            tcs.TrySetCanceled();
        }
    }

    public async Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        if (!IsHealthy)
        {
            throw new InvalidOperationException($"MCP server '{ServerId}' is not healthy; cannot send request.");
        }

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _process.StandardInput.WriteLineAsync(line);
            await _process.StandardInput.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadStdoutLoopAsync()
    {
        try
        {
            while (!_lifetimeCts.IsCancellationRequested)
            {
                var line = await _process.StandardOutput.ReadLineAsync(_lifetimeCts.Token);
                if (line is null)
                {
                    break; // EOF - process closed stdout.
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                // Every per-message failure is contained here: one unusable frame is logged and
                // skipped, and the connection stays usable. Letting it reach the outer catch would
                // end the read loop and fail every pending (and future) request on this server.
                try
                {
                    DispatchLine(line);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Skipping unprocessable JSON-RPC line from MCP server '{ServerId}': {Line}", ServerId, line);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP stdout read loop for '{ServerId}' terminated unexpectedly", ServerId);
        }
        finally
        {
            _faulted = true;
            FailAllPending($"MCP server '{ServerId}' connection closed.");
        }
    }

    private void DispatchLine(string line)
    {
        JsonRpcMessage? message;
        try
        {
            message = JsonRpcCodec.TryDecodeLine(line);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Malformed JSON-RPC line from MCP server '{ServerId}': {Line}", ServerId, line);
            return;
        }

        if (message is null)
        {
            return;
        }

        if (!message.IsResponse)
        {
            _logger.LogDebug("Ignoring unsolicited MCP message from '{ServerId}' (method={Method})", ServerId, message.Method);
            return;
        }

        var idKey = message.IdKey;
        if (idKey is null)
        {
            // A response with a null or non-scalar id cannot be correlated to a request.
            _logger.LogWarning("Ignoring JSON-RPC response with an uncorrelatable id from '{ServerId}': {Line}", ServerId, line);
            return;
        }

        if (_pending.TryRemove(idKey, out var tcs))
        {
            tcs.TrySetResult(message);
        }
        else
        {
            _logger.LogWarning("Received response for unknown/already-completed request id {Id} from '{ServerId}'", idKey, ServerId);
        }
    }

    private async Task ReadStderrLoopAsync()
    {
        try
        {
            while (!_lifetimeCts.IsCancellationRequested)
            {
                var line = await _process.StandardError.ReadLineAsync(_lifetimeCts.Token);
                if (line is null)
                {
                    break;
                }

                if (!string.IsNullOrWhiteSpace(line))
                {
                    _logger.LogInformation("[{ServerId} stderr] {Line}", ServerId, line);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
        catch (Exception)
        {
            // stderr drain is best-effort diagnostics only.
        }
    }

    private void FailAllPending(string reason)
    {
        foreach (var key in _pending.Keys.ToArray())
        {
            if (_pending.TryRemove(key, out var tcs))
            {
                tcs.TrySetException(new IOException(reason));
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetimeCts.Cancel();

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort - process may have already exited between the check and the kill.
        }

        var loops = new[] { _stdoutLoop, _stderrLoop }.Where(t => t is not null).Select(t => t!);
        try
        {
            await Task.WhenAll(loops).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Don't let a stuck reader loop block shutdown.
        }

        FailAllPending($"MCP server '{ServerId}' was disposed.");
        _process.Dispose();
        _writeLock.Dispose();
        _lifetimeCts.Dispose();
    }
}

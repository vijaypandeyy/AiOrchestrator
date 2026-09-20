namespace AiOrchestrator.Core.Options;

/// <summary>Bound from the "Llm:Ollama" section of appsettings.json.</summary>
public sealed class OllamaOptions
{
    public const string SectionName = "Llm:Ollama";

    /// <summary>Base URL of a locally (or remotely) running Ollama server.</summary>
    public string BaseUrl { get; set; } = "http://localhost:11434/";

    /// <summary>Model id, e.g. "llama3.1". Must be a model that has been pulled (`ollama pull &lt;model&gt;`)
    /// and supports tool calling.</summary>
    public string Model { get; set; } = "llama3.1";

    /// <summary>
    /// Local inference can be much slower than a hosted API, especially on CPU-only machines or
    /// the first call after the model is loaded into memory, hence the longer default timeout
    /// compared to <see cref="ClaudeOptions.TimeoutSeconds"/>.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Number of model layers Ollama offloads to the GPU, sent as the "num_gpu" request option.
    /// Leave null to let Ollama decide; set 0 to force CPU-only inference, which avoids driver
    /// crashes ("device lost", "connection forcibly closed") on old or low-VRAM GPUs.
    /// </summary>
    public int? NumGpu { get; set; }

    /// <summary>
    /// Context-window size in tokens, sent as the "num_ctx" request option. Ollama's own default is
    /// small (2k-4k depending on version), and this orchestrator's system prompt plus the full tool
    /// catalog can exceed it - at which point the prompt is silently truncated and tool calling gets
    /// erratic. Leave null to accept the model's default.
    /// </summary>
    public int? NumCtx { get; set; }

    /// <summary>
    /// How long Ollama keeps the model loaded in memory after a request, sent as "keep_alive"
    /// (e.g. "30m", "-1" to keep it loaded indefinitely). Keeping it loaded avoids paying the
    /// multi-second load cost on the first call of every query. Leave null for Ollama's default.
    /// </summary>
    public string? KeepAlive { get; set; }
}

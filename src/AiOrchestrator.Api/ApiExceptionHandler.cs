using AiOrchestrator.Core.Abstractions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace AiOrchestrator.Api;

/// <summary>
/// Turns every unhandled exception into an RFC 7807 problem response with a status code that says
/// whose problem it is: 400 for a bad request, 502/503/429 when the *language model provider* is
/// failing, unreachable or throttling, and 500 only for genuine faults in this service.
///
/// Exception messages are never echoed to callers except where this service authored them (its own
/// validation messages), because upstream error bodies and .NET exception text routinely leak
/// internals - URLs, file paths, configuration and occasionally credentials. The full exception is
/// logged instead.
/// </summary>
public sealed class ApiExceptionHandler : IExceptionHandler
{
    private const int StatusClientClosedRequest = 499; // Nginx's de-facto code; no constant in ASP.NET Core.

    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<ApiExceptionHandler> _logger;

    public ApiExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<ApiExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // The caller hung up: nothing can be written back, and this is not an error worth alerting on.
            _logger.LogInformation("Request {Path} was cancelled by the caller.", httpContext.Request.Path);
            httpContext.Response.StatusCode = StatusClientClosedRequest;
            return true;
        }

        var (status, title, detail) = Map(exception);

        if (status >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Unhandled error while processing {Path}", httpContext.Request.Path);
        }
        else
        {
            _logger.LogWarning(exception, "Request {Path} failed with status {Status}", httpContext.Request.Path, status);
        }

        httpContext.Response.StatusCode = status;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail },
        });
    }

    /// <summary>The status/title/detail an exception is reported as. Public so it can be unit tested
    /// without standing up the whole pipeline.</summary>
    public static (int Status, string Title, string Detail) Map(Exception exception) => exception switch
    {
        // Written by this service, so it is safe (and useful) to pass on verbatim.
        ArgumentException ex => (StatusCodes.Status400BadRequest, "Invalid request", ex.Message),

        LlmProviderException { IsUnreachable: true } ex => (
            StatusCodes.Status503ServiceUnavailable,
            "Language model provider unavailable",
            $"The '{ex.ProviderId}' language model provider could not be reached. Please try again shortly."),

        LlmProviderException { IsThrottled: true } ex => (
            StatusCodes.Status429TooManyRequests,
            "Language model provider is throttling requests",
            $"The '{ex.ProviderId}' language model provider is rate limiting requests. Please retry after a short delay."),

        LlmProviderException ex => (
            StatusCodes.Status502BadGateway,
            "Language model provider error",
            $"The '{ex.ProviderId}' language model provider returned an error (status {ex.StatusCode})."),

        TimeoutException => (
            StatusCodes.Status504GatewayTimeout,
            "Upstream timeout",
            "A dependency did not respond in time. Please try again."),

        _ => (
            StatusCodes.Status500InternalServerError,
            "Unexpected error",
            "The request could not be completed. The failure has been logged."),
    };
}

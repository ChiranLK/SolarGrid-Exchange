/*
 * CorrelationIdMiddleware.cs
 * -----------------------------------------------------------------------------
 * Purpose : Assigns one safe request correlation identifier to response headers,
 *           structured log scope, health output, and unexpected-error diagnosis.
 * Security: Incoming values are accepted only from a small non-sensitive ASCII
 *           character set and are never derived from JWTs, QR values, or PII.
 * -----------------------------------------------------------------------------
 */

namespace SolarMicrogrid.API.Middleware;

public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    private const int MaximumLength = 64;

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(
        RequestDelegate next,
        ILogger<CorrelationIdMiddleware> logger)
    {
        // Capture the next pipeline stage and structured logger once per middleware instance.
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Prefer a validated upstream identifier and otherwise use ASP.NET's generated trace ID.
        string correlationId = GetSafeCorrelationId(context.Request.Headers[HeaderName]);
        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        }))
        {
            await _next(context);
        }
    }

    private static string GetSafeCorrelationId(string? candidate)
    {
        // Reject control characters and log-forging input instead of normalizing attacker data.
        if (!string.IsNullOrWhiteSpace(candidate) &&
            candidate.Length <= MaximumLength &&
            candidate.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
        {
            return candidate;
        }

        return Guid.NewGuid().ToString("N");
    }
}

using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;

namespace Authentication.Api.Security;

/// <summary>The shared <c>429 Too Many Requests</c> response and its security event.</summary>
internal static partial class TooManyRequests
{
    public const string LoggerCategory = "Authentication.Api.Security.RateLimit";

    /// <summary>Writes the problem-details 429 (no account data) and records <c>RateLimitApplied</c>.</summary>
    public static async Task WriteAsync(HttpContext context, string policy, TimeSpan? retryAfter, bool logClientAddress = true)
    {
        ArgumentNullException.ThrowIfNull(context);

        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerCategory);
        var now = context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        var traceId = Activity.Current?.TraceId.ToString();
        var spanId = Activity.Current?.SpanId.ToString();

        if (logClientAddress)
        {
            LogApplied(logger, policy, ClientAddress(context), now, traceId, spanId);
        }
        else
        {
            LogAppliedWithoutAddress(logger, policy, now, traceId, spanId);
        }

        if (retryAfter is { } wait)
        {
            context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        await Results.Problem(
            statusCode: StatusCodes.Status429TooManyRequests,
            title: "Too Many Requests",
            detail: "Too many requests. Try again later.").ExecuteAsync(context);
    }

    /// <summary>The partition key and log value: the effective address with IPv4-mapped IPv6 folded to IPv4.</summary>
    public static string ClientAddress(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address is null)
        {
            return "unknown";
        }

        return (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
    }

    [LoggerMessage(LogLevel.Warning, "RateLimitApplied: request limit '{Policy}' applied to client {ClientAddress} at {OccurredAtUtc:O}; trace {TraceId}, span {SpanId}.")]
    private static partial void LogApplied(ILogger logger, string policy, string clientAddress, DateTimeOffset occurredAtUtc, string? traceId, string? spanId);

    [LoggerMessage(LogLevel.Warning, "RateLimitApplied: request limit '{Policy}' applied at {OccurredAtUtc:O}; trace {TraceId}, span {SpanId}.")]
    private static partial void LogAppliedWithoutAddress(ILogger logger, string policy, DateTimeOffset occurredAtUtc, string? traceId, string? spanId);
}

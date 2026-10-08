using Microsoft.Extensions.Logging;

namespace Authentication.Infrastructure.Identity;

/// <summary>
/// Structured security events for the Identity adapters. They name the real internal cause so operators can
/// tell a failure apart (SRS NFR-SEC-ENUM-005) and carry user ids only: never the submitted email, a
/// password, or any token. The HTTP response stays generic regardless of the cause.
/// </summary>
internal static partial class SecurityEvents
{
    [LoggerMessage(LogLevel.Warning, "LoginFailed: reason {Reason}, user {UserId} at {OccurredAtUtc:O}; trace {TraceId}, span {SpanId}.")]
    public static partial void LoginFailed(ILogger logger, string reason, string userId, DateTimeOffset occurredAtUtc, string? traceId, string? spanId);

    [LoggerMessage(LogLevel.Information, "LoginSucceeded: user {UserId} at {OccurredAtUtc:O}; trace {TraceId}, span {SpanId}.")]
    public static partial void LoginSucceeded(ILogger logger, string userId, DateTimeOffset occurredAtUtc, string? traceId, string? spanId);

    [LoggerMessage(LogLevel.Information, "UserCreated: user {UserId} with {RoleCount} initial roles at {OccurredAtUtc:O}; trace {TraceId}, span {SpanId}.")]
    public static partial void UserCreated(ILogger logger, string userId, int roleCount, DateTimeOffset occurredAtUtc, string? traceId, string? spanId);

    [LoggerMessage(LogLevel.Information, "UserEnabled: user {UserId} at {OccurredAtUtc:O}; trace {TraceId}, span {SpanId}.")]
    public static partial void UserEnabled(ILogger logger, string userId, DateTimeOffset occurredAtUtc, string? traceId, string? spanId);

    [LoggerMessage(LogLevel.Information, "UserDisabled: user {UserId} at {OccurredAtUtc:O}; trace {TraceId}, span {SpanId}.")]
    public static partial void UserDisabled(ILogger logger, string userId, DateTimeOffset occurredAtUtc, string? traceId, string? spanId);

    [LoggerMessage(LogLevel.Information, "UserRoleAssigned: user {UserId}, role {Role} at {OccurredAtUtc:O}; trace {TraceId}, span {SpanId}.")]
    public static partial void UserRoleAssigned(ILogger logger, string userId, string role, DateTimeOffset occurredAtUtc, string? traceId, string? spanId);

    [LoggerMessage(LogLevel.Information, "UserRoleRemoved: user {UserId}, role {Role} at {OccurredAtUtc:O}; trace {TraceId}, span {SpanId}.")]
    public static partial void UserRoleRemoved(ILogger logger, string userId, string role, DateTimeOffset occurredAtUtc, string? traceId, string? spanId);

    [LoggerMessage(LogLevel.Warning, "AccountLockedOut: account {UserId} locked until {LockoutEndUtc:O} (source {Source}) at {OccurredAtUtc:O}; trace {TraceId}, span {SpanId}.")]
    public static partial void AccountLockedOut(ILogger logger, string userId, DateTimeOffset lockoutEndUtc, string source, DateTimeOffset occurredAtUtc, string? traceId, string? spanId);
}

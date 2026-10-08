namespace Authentication.Application.Features.Users;

/// <summary>The administrative view of a user; it never carries credentials, stamps, or tokens.</summary>
public sealed record UserView(
    string Id,
    string Email,
    bool Enabled,
    bool IsLockedOut,
    DateTime? LockoutEndUtc,
    IReadOnlyList<string> Roles);

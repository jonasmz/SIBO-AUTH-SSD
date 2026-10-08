namespace Authentication.Application.Features.Sessions;

public sealed record RefreshSessionRotationResult(bool Succeeded, string? FamilyId, string? UserId, DateTimeOffset? ExpiresAtUtc);

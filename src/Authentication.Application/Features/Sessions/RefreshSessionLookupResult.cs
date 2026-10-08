namespace Authentication.Application.Features.Sessions;

public sealed record RefreshSessionLookupResult(string FamilyId, string UserId, DateTimeOffset ExpiresAtUtc, bool IsConsumed, bool IsRevoked);

namespace Authentication.Domain.Sessions;

public sealed class RenewableSessionFamily
{
    public RenewableSessionFamily(string id, string userId, DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (createdAtUtc.Offset != TimeSpan.Zero || expiresAtUtc.Offset != TimeSpan.Zero || expiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
        }

        Id = id;
        UserId = userId;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    private RenewableSessionFamily() { }

    public string Id { get; private set; } = null!;
    public string UserId { get; private set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public SessionRevocationReason? RevocationReason { get; private set; }

    public bool IsActive(DateTimeOffset nowUtc) => RevokedAtUtc is null && nowUtc < ExpiresAtUtc;

    public void Revoke(DateTimeOffset revokedAtUtc, SessionRevocationReason reason)
    {
        if (revokedAtUtc.Offset != TimeSpan.Zero || RevokedAtUtc is not null)
        {
            return;
        }

        RevokedAtUtc = revokedAtUtc;
        RevocationReason = reason;
    }
}

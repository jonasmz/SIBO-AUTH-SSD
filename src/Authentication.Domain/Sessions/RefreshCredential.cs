namespace Authentication.Domain.Sessions;

public sealed class RefreshCredential
{
    public RefreshCredential(string id, string familyId, byte[] tokenHash, DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(familyId);
        ArgumentNullException.ThrowIfNull(tokenHash);
        if (tokenHash.Length != 32 || createdAtUtc.Offset != TimeSpan.Zero || expiresAtUtc.Offset != TimeSpan.Zero || expiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(tokenHash));
        }

        Id = id; FamilyId = familyId; TokenHash = tokenHash; CreatedAtUtc = createdAtUtc; ExpiresAtUtc = expiresAtUtc;
    }

    private RefreshCredential() { }

    public string Id { get; private set; } = null!;
    public string FamilyId { get; private set; } = null!;
    public byte[] TokenHash { get; private set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public string? ReplacedByTokenId { get; private set; }

    public bool Consume(DateTimeOffset consumedAtUtc, string replacementTokenId)
    {
        if (ConsumedAtUtc is not null || consumedAtUtc.Offset != TimeSpan.Zero || string.IsNullOrWhiteSpace(replacementTokenId)) return false;
        ConsumedAtUtc = consumedAtUtc; ReplacedByTokenId = replacementTokenId; return true;
    }

    public void Revoke(DateTimeOffset revokedAtUtc) { if (RevokedAtUtc is null && revokedAtUtc.Offset == TimeSpan.Zero) RevokedAtUtc = revokedAtUtc; }
}

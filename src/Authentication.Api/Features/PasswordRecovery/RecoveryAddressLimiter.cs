using System.Threading.RateLimiting;
using Authentication.Api.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Authentication.Api.Features.PasswordRecovery;

/// <summary>
/// Limits recovery requests per normalized address (SRS NFR-SEC-BF-010/011), in addition to the per-origin
/// policy, so one mailbox cannot be flooded from many origins. Counters are in process memory; no external store.
/// </summary>
public sealed class RecoveryAddressLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;
    private readonly ILookupNormalizer _normalizer;

    public RecoveryAddressLimiter(IOptions<RateLimitingOptions> options, ILookupNormalizer normalizer)
    {
        ArgumentNullException.ThrowIfNull(options);
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));

        var policy = options.Value.ForgotPasswordAddress;
        _limiter = PartitionedRateLimiter.Create<string, string>(address =>
            RateLimitPartition.GetFixedWindowLimiter(
                address,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = policy.PermitLimit,
                    Window = policy.Window,
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
    }

    /// <summary>Consumes one permit for the address, normalized exactly as the account lookup normalizes it.</summary>
    public (bool Acquired, TimeSpan? RetryAfter) TryAcquire(string email)
    {
        ArgumentNullException.ThrowIfNull(email);

        using var lease = _limiter.AttemptAcquire(_normalizer.NormalizeEmail(email) ?? email);
        if (lease.IsAcquired)
        {
            return (true, null);
        }

        return (false, lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : null);
    }

    public void Dispose() => _limiter.Dispose();
}

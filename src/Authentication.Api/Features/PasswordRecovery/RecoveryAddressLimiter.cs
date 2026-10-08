using System.Threading.RateLimiting;
using Authentication.Api.Security;
using Microsoft.Extensions.Options;

namespace Authentication.Api.Features.PasswordRecovery;

/// <summary>
/// Limits recovery requests per normalized address (SRS NFR-SEC-BF-010/011), in addition to the per-origin
/// policy, so one mailbox cannot be flooded from many origins. Counters are in process memory; no external store.
/// It is a singleton holding the counters, so the caller normalizes the address with its own (scoped) Identity
/// normalizer instead of this type capturing one.
/// </summary>
public sealed class RecoveryAddressLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public RecoveryAddressLimiter(IOptions<RateLimitingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

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

    /// <summary>Consumes one permit for an address already normalized exactly as the account lookup normalizes it.</summary>
    public (bool Acquired, TimeSpan? RetryAfter) TryAcquire(string normalizedEmail)
    {
        ArgumentNullException.ThrowIfNull(normalizedEmail);

        using var lease = _limiter.AttemptAcquire(normalizedEmail);
        if (lease.IsAcquired)
        {
            return (true, null);
        }

        return (false, lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : null);
    }

    public void Dispose() => _limiter.Dispose();
}

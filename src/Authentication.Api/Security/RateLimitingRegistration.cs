using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Authentication.Api.Security;

public static class RateLimitingRegistration
{
    public const string Login = "login";
    public const string Refresh = "refresh";
    public const string ForgotPassword = "forgot-password";
    public const string ResetPassword = "reset-password";
    public const string ForgotPasswordAddress = "forgot-password-address";

    /// <summary>
    /// Registers one independent fixed-window policy per anonymous sensitive endpoint, partitioned by the
    /// effective client address. Counters live in process memory only; no package or external store is used.
    /// </summary>
    public static IServiceCollection AddAuthenticationRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var limits = RateLimitingOptions.FromConfiguration(configuration);
        var invalidSetting = limits.FirstInvalidSetting();
        if (invalidSetting is not null)
        {
            throw new InvalidOperationException($"Required configuration '{invalidSetting}' is missing or invalid.");
        }

        services.AddSingleton(Options.Create(limits));
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(Login, context => ByAddress(context, limits.Login));
            options.AddPolicy(Refresh, context => ByAddress(context, limits.Refresh));
            options.AddPolicy(ForgotPassword, context => ByAddress(context, limits.ForgotPassword));
            options.AddPolicy(ResetPassword, context => ByAddress(context, limits.ResetPassword));
            options.OnRejected = async (rejected, cancellationToken) =>
            {
                var policy = rejected.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName ?? "unknown";
                TimeSpan? retryAfter = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : null;
                await TooManyRequests.WriteAsync(rejected.HttpContext, policy, retryAfter);
            };
        });

        return services;
    }

    private static RateLimitPartition<string> ByAddress(HttpContext context, RateLimitPolicy policy) =>
        RateLimitPartition.GetFixedWindowLimiter(
            TooManyRequests.ClientAddress(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = policy.PermitLimit,
                Window = policy.Window,
                QueueLimit = 0,
                AutoReplenishment = true
            });
}

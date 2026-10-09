using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Authentication.Api.Security;

public static partial class RateLimitingRegistration
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
            // With the switch off the policies stay registered (endpoints reference them) but never limit.
            options.AddPolicy(Login, context => Partition(context, limits, limits.Login));
            options.AddPolicy(Refresh, context => Partition(context, limits, limits.Refresh));
            options.AddPolicy(ForgotPassword, context => Partition(context, limits, limits.ForgotPassword));
            options.AddPolicy(ResetPassword, context => Partition(context, limits, limits.ResetPassword));
            options.OnRejected = async (rejected, cancellationToken) =>
            {
                var policy = rejected.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName ?? "unknown";
                TimeSpan? retryAfter = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : null;
                await TooManyRequests.WriteAsync(rejected.HttpContext, policy, retryAfter);
            };
        });

        return services;
    }

    /// <summary>Warns at startup when request limiting is switched off.</summary>
    public static void WarnIfRateLimitingDisabled(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (!app.Services.GetRequiredService<IOptions<RateLimitingOptions>>().Value.Enabled)
        {
            LogRateLimitingDisabled(app.Logger);
        }
    }

    [LoggerMessage(LogLevel.Warning, "RateLimitingDisabled: application request limits are switched off (RateLimiting:Enabled=false); do not expose this instance.")]
    private static partial void LogRateLimitingDisabled(ILogger logger);

    private static RateLimitPartition<string> Partition(HttpContext context, RateLimitingOptions limits, RateLimitPolicy policy) =>
        limits.Enabled ? ByAddress(context, policy) : RateLimitPartition.GetNoLimiter(string.Empty);

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

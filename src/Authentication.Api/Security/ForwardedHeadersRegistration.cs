using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace Authentication.Api.Security;

public static class ForwardedHeadersRegistration
{
    /// <summary>
    /// Reconstructs the original client address and scheme from <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c>,
    /// only for the configured proxies and networks. The framework pre-trusts loopback, so both lists are cleared
    /// and refilled from configuration; with nothing configured forwarded-header processing is switched off. The chain is
    /// consumed from the right only while each hop is trusted, so entries a client placed before the first
    /// authorized hop are ignored. <c>X-Forwarded-Host</c> is never honored.
    /// </summary>
    public static IServiceCollection AddTrustedForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var proxies = ReverseProxyOptions.FromConfiguration(configuration);
        if (proxies.InvalidSetting is not null)
        {
            throw new InvalidOperationException($"Required configuration '{proxies.InvalidSetting}' is missing or invalid.");
        }

        services.AddSingleton(Options.Create(proxies));
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            // The framework only checks the sender when at least one proxy or network is listed: with both
            // lists empty it would trust every sender. Without configuration nothing is processed at all.
            var anyTrusted = proxies.TrustedProxies.Count > 0 || proxies.TrustedNetworks.Count > 0;
            options.ForwardedHeaders = anyTrusted
                ? ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
                : ForwardedHeaders.None;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var proxy in proxies.TrustedProxies)
            {
                options.KnownProxies.Add(proxy);
            }

            foreach (var network in proxies.TrustedNetworks)
            {
                options.KnownIPNetworks.Add(network);
            }

            options.ForwardLimit = null;
            options.RequireHeaderSymmetry = false;
        });

        return services;
    }
}

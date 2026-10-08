using System.Net;
using Microsoft.Extensions.Configuration;

namespace Authentication.Api.Security;

/// <summary>
/// The explicitly authorized reverse proxies. Forwarded headers are honored only for requests coming from these
/// addresses or networks; empty (the default) honors none (SRS NFR-NET-002/003).
/// </summary>
public sealed class ReverseProxyOptions
{
    public const string SectionName = "ReverseProxy";

    public IReadOnlyList<IPAddress> TrustedProxies { get; init; } = [];

    public IReadOnlyList<IPNetwork> TrustedNetworks { get; init; } = [];

    /// <summary>The setting whose value could not be parsed, or <see langword="null"/>; never the value itself.</summary>
    public string? InvalidSetting { get; init; }

    public static ReverseProxyOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var proxies = new List<IPAddress>();
        var networks = new List<IPNetwork>();
        string? invalid = null;

        foreach (var entry in Split(configuration[$"{SectionName}:TrustedProxies"]))
        {
            if (IPAddress.TryParse(entry, out var address))
            {
                proxies.Add(address);
            }
            else
            {
                invalid ??= $"{SectionName}:TrustedProxies";
            }
        }

        foreach (var entry in Split(configuration[$"{SectionName}:TrustedNetworks"]))
        {
            if (IPNetwork.TryParse(entry, out var network))
            {
                networks.Add(network);
            }
            else
            {
                invalid ??= $"{SectionName}:TrustedNetworks";
            }
        }

        return new ReverseProxyOptions { TrustedProxies = proxies, TrustedNetworks = networks, InvalidSetting = invalid };
    }

    private static string[] Split(string? value) =>
        (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

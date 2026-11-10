using System.Net;
using Microsoft.Extensions.Logging.Abstractions;

// Before .NET 11, IPNetwork is the backport in IPNetwork.cs

#if NET11_0_OR_GREATER
using IPNetwork = System.Net.IPNetwork;
#endif

namespace Piton.IPRestrictions.Endpoints;

/// <summary>
/// Options for endpoint IP Restrictions middleware.
/// </summary>
public partial class IPRestrictionsOptions
{
    /// <summary>
    /// The IP restriction mode. Default is <see cref="IPRestrictionMode.Enabled"/>.
    /// </summary>
    public IPRestrictionMode Mode { get; set; } = IPRestrictionMode.Enabled;

    /// <summary>
    /// A list of IP addresses or IP address ranges that are allowed to access the application.
    /// </summary>
    public List<string> AllowedIPs { get; set; } = [];

    /// <summary>
    /// Whether to allow LAN IP addresses in addition to <see cref="AllowedIPs" />. Default is <see langword="false" />
    /// </summary>
    public bool AllowLanIPs { get; set; }
    
    /// <summary>
    /// Whether to allow loopback IP addresses in addition to <see cref="AllowedIPs" />. Default is <see langword="true" />
    /// </summary>
    public bool AllowLoopbackIPs { get; set; } = true;
    
    /// <summary>
    /// The name of the header to add to the response when <see cref="Mode"/> includes <see cref="IPRestrictionMode.AddEndpointHeader"/>.
    /// The default is <c>X-Remote-Endpoint</c>.
    /// </summary>
    public string RemoteEndpointHeaderName { get; set; } = "X-Remote-Endpoint";

    /// <summary>
    /// Which metrics to record. By default, only denied requests are counted.
    /// </summary>
    public IPRestrictionsMetricsOptions Metrics { get; set; } = new();

    /// <summary>
    /// Whether the middleware logs under the <c>Microsoft.AspNetCore.IPRestrictions</c> category, like ASP.NET Core's
    /// own middleware, so that the usual <c>"Microsoft.AspNetCore": "Warning"</c> logging configuration applies.
    /// When <see langword="false"/>, it logs under <c>Piton.IPRestrictions.Endpoints.IPRestrictionsMiddleware</c>.
    /// Default is <see langword="true" />.
    /// </summary>
    /// <remarks>
    /// Problems with these options (such as an invalid entry in <see cref="AllowedIPs"/>) are always logged under
    /// <c>Piton.IPRestrictions.Endpoints.IPRestrictionsOptions</c>.
    /// </remarks>
    public bool UseMicrosoftLoggingNamespace { get; set; } = true;

    internal bool Validate()
    {
        // Any combination of the flags
        const IPRestrictionMode allModes =
            IPRestrictionMode.Disabled | IPRestrictionMode.AddEndpointHeader | IPRestrictionMode.Enabled;
        if ((Mode & ~allModes) != 0)
            throw new Exception($"{GetType().Name}.{nameof(Mode)}: unexpected value {Mode}");
        
        if (string.IsNullOrEmpty(RemoteEndpointHeaderName))
            throw new Exception($"{GetType().Name}.{nameof(RemoteEndpointHeaderName)} cannot be null or empty");
        
        if (AllowedIPs == null)
            throw new Exception($"{GetType().Name}.{nameof(AllowedIPs)} cannot be null");

        if (Metrics == null)
            throw new Exception($"{GetType().Name}.{nameof(Metrics)} cannot be null");

        return true;
    }

    // Fields, not properties: they are parsed from AllowedIPs, never bound from configuration,
    // and the configuration binder (and its source generator) only considers properties.

    internal List<IPAddress>? AllowedIPAddresses;

    internal List<IPNetwork>? AllowedIPNetworks;

    internal void ParseIPAddresses(ILogger<IPRestrictionsOptions>? logger)
    {
        logger ??= NullLogger<IPRestrictionsOptions>.Instance;
        
        // Remove duplicates and create a copy (for modification)
        var allowedIPs = AllowedIPs.Distinct().ToHashSet();
        
        if (AllowLanIPs)
        {
            LogAddingLanIPRangesToAllowedIps(logger);

            // Class A
            allowedIPs.Add("10.0.0.0/8");
            allowedIPs.Add("::ffff:10.0.0.0/104");
            // Class B
            allowedIPs.Add("172.16.0.0/12");
            allowedIPs.Add("::ffff:172.16.0.0/108");
            // Class C
            allowedIPs.Add("192.168.0.0/16");
            allowedIPs.Add("::ffff:192.168.0.0/112");
        }
        
        if (AllowLoopbackIPs)
        {
            LogAddingLoopbackIPRangesToAllowedIps(logger);
            
            // IPv4 loopback
            allowedIPs.Add("127.0.0.0/8");
            // IPv6 loopback
            allowedIPs.Add("::1/128");
        }
        
        // Parse AllowedIPs into AllowedIPAddresses and AllowedIPNetworks
        AllowedIPAddresses = [];
        AllowedIPNetworks = [];
        
        foreach (var ipString in allowedIPs)
        {
            if (IPAddress.TryParse(ipString, out var ipAddress))
            {
                if (!AllowedIPAddresses.Any(ip => ip.Equals(ipAddress)))
                    AllowedIPAddresses.Add(ipAddress);
            }
            else if (IPNetwork.TryParse(ipString, out var ipNetwork))
            {
                if (!AllowedIPNetworks.Any(n => n.Equals(ipNetwork)))
                    AllowedIPNetworks.Add(ipNetwork);
            }
            else
            {
                LogInvalidIPAddressNetworkInOptionsType(logger, GetType().FullName, nameof(allowedIPs), ipString);
            }
        }

        if (AllowedIPAddresses.Count != 0 || AllowedIPNetworks.Count != 0) 
            return;

        // No valid IP addresses or networks found, log message
        // Restrictions are applied when Enabled is set, unless Disabled is too
        if ((Mode & (IPRestrictionMode.Disabled | IPRestrictionMode.Enabled)) == IPRestrictionMode.Enabled)
            LogNoAllowedIPAddressesOrNetworksFoundAllRequestsWillBeDenied(logger, GetType().FullName);
        else
            LogNoAllowedIPAddressesOrNetworksFoundMiddlewareIsDisabled(logger, GetType().FullName);
    }

    [LoggerMessage(1, LogLevel.Debug, "Adding LAN IP ranges to allowed IPs")]
    static partial void LogAddingLanIPRangesToAllowedIps(ILogger logger);

    [LoggerMessage(2, LogLevel.Debug, "Adding loopback IP ranges to allowed IPs")]
    static partial void LogAddingLoopbackIPRangesToAllowedIps(ILogger logger);
    
    [LoggerMessage(3, LogLevel.Warning, "Invalid IP address/network in {OptionsType}.{AllowedIPs}: {IPAddress}, skipping")]
    static partial void LogInvalidIPAddressNetworkInOptionsType(ILogger logger, string? optionsType, string allowedIPs, string ipAddress);

    [LoggerMessage(4, LogLevel.Warning, "No allowed IP addresses or networks found in {OptionsType}, all requests will be denied")]
    static partial void LogNoAllowedIPAddressesOrNetworksFoundAllRequestsWillBeDenied(ILogger logger, string? optionsType);

    [LoggerMessage(5, LogLevel.Debug, "No allowed IP addresses or networks found in {OptionsType}, but middleware is disabled so all requests will be allowed")]
    static partial void LogNoAllowedIPAddressesOrNetworksFoundMiddlewareIsDisabled(ILogger logger, string? optionsType);
}
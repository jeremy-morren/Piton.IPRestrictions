using System.Net;
using Microsoft.Extensions.Logging.Abstractions;

// Before .NET 11, IPNetwork is the backport in IPNetwork.cs

#if NET11_0_OR_GREATER
using IPNetwork = System.Net.IPNetwork;
#endif

namespace AspNetCore.IPRestrictions.Endpoints;

/// <summary>
/// Options for endpoint IP Restrictions middleware.
/// </summary>
public partial class IPRestrictionsOptions
{
    /// <summary>
    /// Whether endpoint IP Restrictions middleware is enabled. Default is <see langword="true" />.
    /// </summary>
    public IPRestrictionMode Enabled { get; set; } = IPRestrictionMode.Enabled;

    /// <summary>
    /// A list of IP addresses or IP address ranges that are allowed to access the application.
    /// </summary>
    public List<string> AllowedIPs { get; set; } = [];

    /// <summary>
    /// Whether to allow LAN IP addresses in addition to the specified AllowedIPs.
    /// </summary>
    public bool AllowLanIPs { get; set; }
    
    /// <summary>
    /// Whether to allow loopback IP addresses in addition to the specified AllowedIPs. Default is <see langword="true" />
    /// </summary>
    public bool AllowLoopbackIPs { get; set; } = true;

    /// <summary>
    /// If true, the middleware will add a header with the name specified in <see cref="RemoteEndpointHeaderName"/>
    /// and the value of the remote IP address to all HTTP responses.
    /// </summary>
    public bool AddRemoteEndpointHeader { get; set; }
    
    /// <summary>
    /// The name of the header to add to the response when <see cref="AddRemoteEndpointHeader"/> is true.
    /// The default is <c>X-Remote-Endpoint</c>.
    /// </summary>
    public string RemoteEndpointHeaderName { get; set; } = "X-Remote-Endpoint";

    internal bool Validate()
    {
        if (Enabled != IPRestrictionMode.Disabled && 
            Enabled != IPRestrictionMode.AddEndpointHeader && 
            Enabled != IPRestrictionMode.Enabled)
            throw new Exception($"{GetType().Name}.{nameof(Enabled)}: unexpected value {Enabled}");
        
        if (string.IsNullOrEmpty(RemoteEndpointHeaderName))
            throw new Exception($"{GetType().Name}.{nameof(RemoteEndpointHeaderName)} cannot be null or empty");
        
        if (AllowedIPs == null)
            throw new Exception($"{GetType().Name}.{nameof(AllowedIPs)} cannot be null");
        
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
        if (Enabled == IPRestrictionMode.Enabled)
            LogNoAllowedIPAddressesOrNetworksFoundAllRequestsWillBeDenied(logger, GetType().FullName);
        else
            LogNoAllowedIPAddressesOrNetworksFoundMiddlewareIsDisabled(logger, GetType().FullName);
    }

    [LoggerMessage(1, LogLevel.Debug, "Adding LAN IP ranges to allowed IPs")]
    static partial void LogAddingLanIPRangesToAllowedIps(ILogger<IPRestrictionsOptions> logger);

    [LoggerMessage(2, LogLevel.Debug, "Adding loopback IP ranges to allowed IPs")]
    static partial void LogAddingLoopbackIPRangesToAllowedIps(ILogger<IPRestrictionsOptions> logger);
    
    [LoggerMessage(3, LogLevel.Warning, "Invalid IP address/network in {OptionsType}.{AllowedIPs}: {IPAddress}, skipping")]
    static partial void LogInvalidIPAddressNetworkInOptionsType(ILogger<IPRestrictionsOptions> logger, string? optionsType, string allowedIPs, string ipAddress);

    [LoggerMessage(4, LogLevel.Warning, "No allowed IP addresses or networks found in {OptionsType}, all requests will be denied")]
    static partial void LogNoAllowedIPAddressesOrNetworksFoundAllRequestsWillBeDenied(ILogger<IPRestrictionsOptions> logger, string? optionsType);

    [LoggerMessage(5, LogLevel.Debug, "No allowed IP addresses or networks found in {OptionsType}, but middleware is disabled so all requests will be allowed")]
    static partial void LogNoAllowedIPAddressesOrNetworksFoundMiddlewareIsDisabled(ILogger<IPRestrictionsOptions> logger, string? optionsType);
}
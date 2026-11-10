using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Options;

#if NET11_0_OR_GREATER
using IPNetwork = System.Net.IPNetwork;
#endif
// Before .NET 11, IPNetwork is the backport in IPNetwork.cs

#if NET9_0_OR_GREATER
using Microsoft.AspNetCore.StaticAssets;
#endif

namespace AspNetCore.IPRestrictions.Endpoints;

/// <summary>
/// Middleware to restrict access based on IP address.
/// </summary>
internal partial class IPRestrictionsMiddleware : IMiddleware
{
    private readonly IOptionsMonitor<IPRestrictionsOptions> _options;
    private readonly IPRestrictionsInstrumentation _instrumentation;
    private readonly ILogger<IPRestrictionsMiddleware> _logger;

    public IPRestrictionsMiddleware(
        IOptionsMonitor<IPRestrictionsOptions> options,
        IPRestrictionsInstrumentation instrumentation,
        ILogger<IPRestrictionsMiddleware> logger)
    {
        _options = options;
        _instrumentation = instrumentation;
        _logger = logger;
    }

    public Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var options = _options.CurrentValue;

        // Add the remote endpoint header if configured to do so, even if the middleware is disabled
        var remoteIp = context.Connection.RemoteIpAddress;
        if (options.Enabled is IPRestrictionMode.AddEndpointHeader or IPRestrictionMode.Enabled 
            && options.AddRemoteEndpointHeader
            && remoteIp != null)
        {
            context.Response.Headers[options.RemoteEndpointHeaderName] = remoteIp.ToString();
            LogAddedXRemoteEndpointHeader(options.RemoteEndpointHeaderName, remoteIp);
        }
        
        if (options.Enabled != IPRestrictionMode.Enabled)
        {
            LogIPRestrictionsMiddlewareIsDisabledAllowingAllRequests();
            return next(context);
        }
        
        var endpoint = context.GetEndpoint();
        if (endpoint == null)
        {
            LogNoEndpointForRequestIgnoringIPRestrictions(context.Request.Path);
            return next(context);
        }

        var (displayName, path) = (GetEndpointDisplayName(endpoint), context.Request.Path);
        if (_logger.IsEnabled(LogLevel.Trace))
            LogProcessingRequestForEndpointPath(displayName, path);

        if (!HasRestrictIPAttribute(endpoint.Metadata, out var predicate))
        {
            LogIgnoringIPRestrictionsForEndpointPath(displayName, path);
            return next(context);
        }

        // A request to an endpoint with IP restrictions, whether or not the predicate exempts it
        _instrumentation.Request(options.Metrics, endpoint, remoteIp);

        if (predicate != null)
        {
            // Predicate is defined. If it returns false, ignore IP restriction

            LogEvaluatingIPRestrictionPredicate();
            if (!predicate(context))
            {
                // Predicate returned false, ignore IP restriction
                LogIgnoringIPRestrictionsDueToPredicate(displayName, path);
                return next(context);
            }

            // Predicate returned true, apply IP restriction
            LogApplyingIPRestrictionsDueToPredicate(displayName, path);
        }

        // Evaluate IP restrictions

        if (!IsAllowedIP(options, remoteIp, out var matchedAddress, out var matchedNetwork))
        {
            LogForbiddenRequestFromIP(remoteIp);
            _instrumentation.Denied(options.Metrics, endpoint, remoteIp);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.Headers.ContentType = "text/plain";
            return context.Response.WriteAsync("Access denied", cancellationToken: context.RequestAborted);
        }

        Debug.Assert(remoteIp != null);
        if (matchedAddress != null)
        {
            Debug.Assert(matchedAddress.Equals(remoteIp));
            LogRequestFromRemoteIPMatchedAllowedIP(remoteIp, displayName, path);
        }
        else
        {
            Debug.Assert(matchedNetwork != null && matchedNetwork.Value.Contains(remoteIp));
            LogRequestFromRemoteIPMatchedAllowedNetwork(remoteIp, Format(matchedNetwork.Value), displayName, path);
        }

        _instrumentation.Allowed(options.Metrics, endpoint, remoteIp);
        return next(context);
    }

    public static bool IsAllowedIP(IPRestrictionsOptions options, IPAddress? remoteIp, out IPAddress? matchedAddress, out IPNetwork? matchedNetwork)
    {
        matchedAddress = null;
        matchedNetwork = null;
        if (remoteIp == null)
            return false;
        Debug.Assert(options is { AllowedIPAddresses: not null, AllowedIPNetworks: not null }, "Options not initialized");

        foreach (var ip in options.AllowedIPAddresses)
        {
            if (IsSameAddress(ip, remoteIp))
            {
                matchedAddress = ip;
                return true;
            }
        }

        foreach (var network in options.AllowedIPNetworks)
        {
            if (network.Contains(remoteIp))
            {
                matchedNetwork = network;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether two addresses are the same host, treating an IPv4 mapped IPv6 address as the IPv4 address it stands for.
    /// </summary>
    /// <remarks>
    /// Kestrel reports an IPv4 client as ::ffff:a.b.c.d when it listens on a dual mode socket.
    /// IPAddress.Equals compares the family first, so ::ffff:203.0.113.5 does not equal 203.0.113.5
    /// and an allowed address written the plain way would never match.
    /// </remarks>
    private static bool IsSameAddress(IPAddress allowed, IPAddress remote) =>
        allowed.Equals(remote) || Unmap(allowed).Equals(Unmap(remote));

    /// <summary>
    /// Unmaps an IPv4 mapped IPv6 address to the IPv4 address it stands for, otherwise returns the address unchanged.
    /// </summary>
    private static IPAddress Unmap(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    /// <summary>
    /// Whether the endpoint has a <see cref="RestrictIPAttribute"/> in its metadata, and if so, returns the predicate from the attribute.
    /// </summary>
    private static bool HasRestrictIPAttribute(EndpointMetadataCollection metadata, out Predicate<HttpContext>? predicate)
    {
        var attribute = metadata.GetMetadata<RestrictIPAttribute>();
        predicate = attribute?.Predicate;
        return attribute != null;
    }

    /// <summary>
    /// Formats a network in CIDR notation, e.g. <c>10.0.0.0/8</c>.
    /// </summary>
    public static string Format(IPNetwork network)
    {
        return $"{network.BaseAddress}/{network.PrefixLength}";
    }

    private static string? GetEndpointDisplayName(Endpoint endpoint)
    {
        if (endpoint.DisplayName != null)
            return endpoint.DisplayName;
        
#if NET9_0_OR_GREATER
        // If the endpoint is a static asset, use the asset path as the display name
        var staticAsset = endpoint.Metadata.GetMetadata<StaticAssetDescriptor>();
        if (staticAsset != null)
            return $"StaticAsset: {staticAsset.AssetPath}";
#endif
        
        return endpoint.ToString();
    }

    [LoggerMessage(10, LogLevel.Trace, "Added remote endpoint header {HeaderName} with value {RemoteIp}")]
    partial void LogAddedXRemoteEndpointHeader(string headerName, IPAddress remoteIp);

    [LoggerMessage(11, LogLevel.Debug, "No endpoint for request, ignoring IP restrictions. Path: {Path}")]
    partial void LogNoEndpointForRequestIgnoringIPRestrictions(PathString path);

    [LoggerMessage(12, LogLevel.Trace, "Processing request for endpoint {Endpoint}. Path: {Path}")]
    partial void LogProcessingRequestForEndpointPath(string? endpoint, PathString path);

    [LoggerMessage(13, LogLevel.Debug, "Ignoring IP restrictions for endpoint {Endpoint}. Path: {Path}")]
    partial void LogIgnoringIPRestrictionsForEndpointPath(string? endpoint, PathString path);

    [LoggerMessage(14, LogLevel.Trace, "Evaluating IP restriction predicate")]
    partial void LogEvaluatingIPRestrictionPredicate();

    [LoggerMessage(15, LogLevel.Debug, "Ignoring IP restrictions due to predicate for endpoint {Endpoint}. Path: {Path}")]
    partial void LogIgnoringIPRestrictionsDueToPredicate(string? endpoint, PathString path);

    [LoggerMessage(16, LogLevel.Debug, "Applying IP restrictions due to predicate for endpoint {Endpoint}. Path: {Path}")]
    partial void LogApplyingIPRestrictionsDueToPredicate(string? endpoint, PathString path);

    [LoggerMessage(17, LogLevel.Debug, "Forbidden request from IP {RemoteIP}")]
    partial void LogForbiddenRequestFromIP(IPAddress? remoteIp);

    [LoggerMessage(18, LogLevel.Debug, "Request from {RemoteIp} matched allowed IP. Endpoint: {Endpoint}. Path: {Path}")]
    partial void LogRequestFromRemoteIPMatchedAllowedIP(IPAddress remoteIp, string? endpoint, PathString path);

    [LoggerMessage(19, LogLevel.Debug, "Request from {RemoteIp} matched allowed network {AllowedNetwork}. Endpoint: {Endpoint}. Path: {Path}")]
    partial void LogRequestFromRemoteIPMatchedAllowedNetwork(IPAddress remoteIp, string allowedNetwork, string? endpoint, PathString path);

    [LoggerMessage(20, LogLevel.Debug, "IP Restrictions middleware is disabled, allowing all requests")]
    partial void LogIPRestrictionsMiddlewareIsDisabledAllowingAllRequests();
}
using Microsoft.AspNetCore.HttpOverrides;

namespace AspNetCore.IPRestrictions.Endpoints;

public static class ForwardedHeadersOptionsIPRestrictionsExtensions
{
    /// <summary>
    /// Configures the <see cref="ForwardedHeadersOptions"/> to use the correct value for
    /// <see cref="ForwardedHeadersOptions.ForwardedForHeaderName"/> based on the specified reverse proxy.
    /// </summary>
    /// <param name="options">The <see cref="ForwardedHeadersOptions"/> to configure.</param>
    /// <param name="reverseProxy">The reverse proxy for which to configure the forwarded headers options.</param>
    /// <returns>The configured <see cref="ForwardedHeadersOptions"/>.</returns>
    /// <remarks>
    /// Some reverse proxies set a custom header for the client IP address.
    /// Some even append the value to the <c>X-Forwarded-For</c> header instead of replacing it, meaning that the <c>X-Forwarded-For</c> header can be spoofed by the client.
    /// This method configures the <see cref="ForwardedHeadersOptions"/> to use the correct header for the specified reverse proxy.
    /// </remarks>
    public static ForwardedHeadersOptions ConfigureForReverseProxy(this ForwardedHeadersOptions options, ReverseProxy reverseProxy)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.ForwardedForHeaderName = reverseProxy switch
        {
            ReverseProxy.Envoy => "x-envoy-external-address",
            ReverseProxy.Cloudflare => "cf-connecting-ip",
            ReverseProxy.Traefik => "X-Real-Ip",
            ReverseProxy.YARP => "X-Forwarded-For",
            ReverseProxy.Caddy => "X-Forwarded-For",
            ReverseProxy.AzureFrontDoor => "X-Azure-SocketIP",
            _ => throw new ArgumentOutOfRangeException(nameof(reverseProxy), reverseProxy, null)
        };

        return options;
    }

    /// <summary>
    /// Adds the correct value to <see cref="ForwardedHeadersOptions.KnownIPNetworks"/> for the current environment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If the application is running in Azure Container Apps, adds the default <c>100.64/10</c> RFC6598 CG-NAT private network.
    /// </para>
    /// <para>
    /// If the application is running in Kubernetes, adds the default <c>10.0/8</c> private network.
    /// </para>
    /// </remarks>
    public static ForwardedHeadersOptions AddKnownIPNetwork(this ForwardedHeadersOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CONTAINER_APP_REPLICA_NAME")))
            // App is running in Azure Container Apps, default is 100.64/10 RFC6598 CG-NAT private network
            AddNetwork("100.64.0.0/10");
        else if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST")))
            // App is running in Kubernetes, default is 10.0/8 private network
            AddNetwork("10.0.0.0/8");

        return options;
        
        void AddNetwork(string cidr)
        {
            var network = IPNetwork.Parse(cidr);
#if NET10_0_OR_GREATER
            options.KnownIPNetworks.Add(new System.Net.IPNetwork(network.BaseAddress, network.PrefixLength));
#else
            options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(network.BaseAddress, network.PrefixLength));
#endif
        }
    }
}
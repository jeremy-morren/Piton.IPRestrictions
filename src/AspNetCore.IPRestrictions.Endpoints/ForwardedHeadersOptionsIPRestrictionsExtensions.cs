#pragma warning disable CS1574

namespace AspNetCore.IPRestrictions.Endpoints;

/// <summary>
/// Options extensions for <see cref="ForwardedHeadersOptions"/>
/// </summary>
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
    /// Detected by the <c>CONTAINER_APP_REPLICA_NAME</c>
    /// <see href="https://learn.microsoft.com/azure/container-apps/environment-variables#built-in-environment-variables">built-in environment variable</see>.
    /// </para>
    /// <para>
    /// If the application is running in Azure App Service, adds the private <c>10.0/8</c>, <c>172.16/12</c> and <c>192.168/16</c> networks,
    /// as <see href="https://learn.microsoft.com/azure/app-service/configure-language-dotnetcore#detect-https-session">documented for the App Service load balancer</see>.
    /// Detected by the <c>WEBSITE_SITE_NAME</c>
    /// <see href="https://learn.microsoft.com/azure/app-service/reference-app-settings#app-environment">app environment variable</see>.
    /// </para>
    /// <para>
    /// If the application is running in Kubernetes, adds the default <c>10.0/8</c> private network.
    /// The pod network depends on the cluster's configuration, so this may need to be changed.
    /// Detected by the <c>KUBERNETES_SERVICE_HOST</c>
    /// <see href="https://kubernetes.io/docs/concepts/services-networking/service/#environment-variables">service environment variable</see>
    /// of the Kubernetes API service.
    /// </para>
    /// </remarks>
    public static ForwardedHeadersOptions AddKnownIPNetwork(this ForwardedHeadersOptions options) =>
        options.AddKnownIPNetwork(Environment.GetEnvironmentVariable);

    internal static ForwardedHeadersOptions AddKnownIPNetwork(this ForwardedHeadersOptions options, Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (IsSet("CONTAINER_APP_REPLICA_NAME"))
        {
            // App is running in Azure Container Apps, default is 100.64/10 RFC6598 CG-NAT private network
            // If the app is running in a private network, this may not be the correct network
            AddNetwork("100.64.0.0/10");
        }
        else if (IsSet("WEBSITE_SITE_NAME"))
        {
            // App is running in Azure App Service: Microsoft documents these private networks for its load balancer,
            // as IPv4-mapped IPv6 networks (Kestrel's dual mode socket); the IPv4 networks cover an IPv4-only socket
            AddNetwork("::ffff:10.0.0.0/104");
            AddNetwork("::ffff:192.168.0.0/112");
            AddNetwork("::ffff:172.16.0.0/108");
            AddNetwork("10.0.0.0/8");
            AddNetwork("192.168.0.0/16");
            AddNetwork("172.16.0.0/12");
        }
        else if (IsSet("KUBERNETES_SERVICE_HOST"))
        {
            // App is running in Kubernetes, default is 10.0/8 private network
            AddNetwork("10.0.0.0/8");
        }

        return options;

        bool IsSet(string variable) => !string.IsNullOrEmpty(getEnvironmentVariable(variable));
        
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
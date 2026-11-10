namespace Piton.IPRestrictions.Endpoints;

/// <summary>
/// Known reverse proxies that set a custom header for the client IP address.
/// </summary>
public enum ReverseProxy
{
    /// <summary>
    /// <see href="https://www.envoyproxy.io/">Envoy</see>: <c>x-envoy-external-address</c> header
    /// (see <see href="https://www.envoyproxy.io/docs/envoy/latest/configuration/http/http_conn_man/headers#x-envoy-external-address">Envoy HTTP headers</see>)
    /// </summary>
    /// <remarks>
    /// <para>
    /// Envoy only sets this header when <c>use_remote_address</c> is <c>true</c> (i.e. it is the edge proxy),
    /// and only for external requests: a request from a private (RFC1918 or RFC4193) address without <c>X-Forwarded-For</c> is internal.
    /// </para>
    /// <para>
    /// Used by Azure Container Apps.
    /// </para>
    /// </remarks>
    Envoy = 1,

    /// <summary>
    /// <see href="https://developers.cloudflare.com/fundamentals">Cloudflare</see>: <c>cf-connecting-ip</c> header
    /// (see <see href="https://developers.cloudflare.com/fundamentals/reference/http-headers/#cf-connecting-ip">Cloudflare HTTP Headers</see>)
    /// </summary>
    /// <remarks>
    /// Also works with <see href="https://developers.cloudflare.com/tunnel/">Cloudflare Tunnel</see>.
    /// </remarks>
    Cloudflare = 2,

    /// <summary>
    /// <see href="https://doc.traefik.io/">Traefik</see>: <c>X-Real-Ip</c> header
    /// (see <see href="https://doc.traefik.io/traefik/reference/routing-configuration/http/middlewares/headers/#opt-Clients-IP">Traefik HTTP Headers</see>)
    /// </summary>
    Traefik = 3,

    /// <summary>
    /// <see href="https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/getting-started">YARP</see>: <c>X-Forwarded-For</c> header
    /// (see <see href="https://learn.microsoft.com/aspnet/core/fundamentals/servers/yarp/header-guidelines#x-forwarded--forwarded">YARP X-Forwarded-For</see>)
    /// </summary>
    /// <remarks>
    /// By default YARP removes any incoming <c>X-Forwarded-For</c> header and replaces it with the client IP address,
    /// so it cannot be spoofed by the client.
    /// </remarks>
    YARP = 4,

    /// <summary>
    /// <see href="https://caddyserver.com/">Caddy</see>: <c>X-Forwarded-For</c> header
    /// (see <see href="https://caddyserver.com/docs/caddyfile/directives/reverse_proxy#defaults">Caddy reverse_proxy defaults</see>)
    /// </summary>
    /// <remarks>
    /// By default Caddy ignores incoming <c>X-Forwarded-*</c> values unless the request comes from one of its <c>trusted_proxies</c>,
    /// so <c>X-Forwarded-For</c> cannot be spoofed by the client.
    /// Caddy does not set <c>X-Real-IP</c>: it passes other headers through unmodified, so a client-supplied <c>X-Real-IP</c> would reach the app.
    /// </remarks>
    Caddy = 5,

    /// <summary>
    /// <see href="https://learn.microsoft.com/azure/frontdoor/front-door-overview">Azure Front Door</see>: <c>X-Azure-SocketIP</c> header
    /// (see <see href="https://learn.microsoft.com/azure/frontdoor/front-door-http-headers-protocol#from-the-front-door-to-the-backend">Azure Front Door HTTP headers</see>)
    /// </summary>
    /// <remarks>
    /// <c>X-Azure-SocketIP</c> is the IP address of the TCP connection to Azure Front Door.
    /// </remarks>
    AzureFrontDoor = 6,
}

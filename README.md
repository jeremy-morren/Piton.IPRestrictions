# ASP.Net Core IP restrictions

[![CI](https://img.shields.io/github/actions/workflow/status/jeremy-morren/Piton.IPRestrictions/draft.yml?branch=main&label=CI)](https://github.com/jeremy-morren/Piton.IPRestrictions/actions/workflows/draft.yml?query=branch%3Amain)
[![Piton.IPRestrictions.Endpoints](https://img.shields.io/nuget/v/Piton.IPRestrictions.Endpoints?label=Piton.IPRestrictions.Endpoints)](https://www.nuget.org/packages/Piton.IPRestrictions.Endpoints)
[![Piton.IPRestrictions.Endpoints.Telemetry](https://img.shields.io/nuget/v/Piton.IPRestrictions.Endpoints.Telemetry?label=Piton.IPRestrictions.Endpoints.Telemetry)](https://www.nuget.org/packages/Piton.IPRestrictions.Endpoints.Telemetry)

Restricts endpoints to a list of addresses and networks. Opt in per endpoint, 
either with `[RestrictIP]` or `.RestrictIP()` on an endpoint convention builder, optionally with a
predicate deciding per request whether the restriction applies.

### Installation

Install the `Piton.IPRestrictions.Endpoints` NuGet package: `dotnet add package Piton.IPRestrictions.Endpoints`

```xml
<Project>
  <ItemGroup>
    <PackageReference Include="Piton.IPRestrictions.Endpoints" Version="..." />
  </ItemGroup>
</Project>
```

### Usage

Configure in `Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add the middleware services
builder.Services.AddIPRestrictions(o => 
{
    // Values can be either IP addresses or CIDR networks.
    o.AllowedIPs.Add("8.8.8.8");
    o.AllowedIPs.Add("198.51.100.0/24");
    
    o.AllowLANIPs = false; // Allow LAN addresses, default is false
});

app.Services.Configure<ForwardedHeadersOptions>(o =>
{
    // When behind a reverse proxy forwarded headers must be enabled, 
    // otherwise the remote address will always be the proxy's address.
    // See section Forwarded Headers below
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ConfigureForReverseProxy(ReverseProxy.YourProxy);
    o.AddKnownNetworkForCurrentEnvironment();
});

// ... other services

var app = builder.Build();

app.UseForwardedHeaders();

// ... other middleware

app.UseRouting();

// The middleware must be added after UseRouting. Any middleware added before UseIPRestrictions 
// (e.g. authentication and authorization) runs first, even for requests it then denies.
app.UseIPRestrictions();

app.UseAuthentication();

// ... other middleware
```

### Endpoints

Configuration is per endpoint, either with an attribute or a convention:

```csharp
// Entire Controller
[RestrictIP]
public class Controller1 : ControllerBase
{
    public IActionResult Get() => Ok();
}

public class Controller2 : ControllerBase
{
    // Individual action
    [RestrictIP]
    public IActionResult Get() => Ok();
}

// Minimal API
app.MapGet("/minimal", () => "Hello, world!").RestrictIP();

// All controllers
app.MapControllers().RestrictIP();

// With a predicate: only applies the restriction if the predicate is matched
app.MapControllers().RestrictIP(context => context.Request.Method != HttpMethods.Get);

```

### Forwarded Headers

There are 2 things to be aware of when using this library behind a reverse proxy:
- Many reverse proxies (like Nginx, Cloudflare and Envoy) **append** (not override) the original client IP address 
  to the `X-Forwarded-For` header, allowing a client to spoof their IP address by adding a fake `X-Forwarded-For` header.
- Since .NET 8, forwarded headers are only applied for known proxies. If your reverse proxy is not in the list of known 
  proxies, the forwarded headers middleware will not apply the forwarded headers.

To help with this, the library contains 2 helper functions:
- `ConfigureForReverseProxy` - configures the forwarded headers options for a specific reverse proxy (see [Reverse Proxies](#reverse-proxies) below)
- `AddKnownNetworkForCurrentEnvironment` - adds the correct value to `ForwardedHeadersOptions.KnownIPNetworks` for certain known environments
  (see [Known Environments](#known-environments) below).

```csharp
app.UseForwardedHeaders(new ForwardedHeadersOptions()
    { ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor }
    .ConfigureForReverseProxy(ReverseProxy.YourProxy) // Configure for the specific reverse proxy you are using
    .AddKnownNetworkForCurrentEnvironment(); // Adds the correct value to ForwardedHeadersOptions.KnownIPNetworks for the current environment
```

### Logging

By default the middleware logs allowed and denied requests (at `Debug`) under the `Microsoft.AspNetCore.IPRestrictions`
category, like ASP.NET Core's own middleware, so the usual `"Microsoft.AspNetCore": "Warning"` logging configuration
applies to it too. To log under the middleware's own category (`Piton.IPRestrictions.Endpoints.IPRestrictionsMiddleware`), 
set `IPRestrictionsOptions.UseMicrosoftLoggingNamespace` to `false`.

Configuration problems (such as an invalid entry in `AllowedIPs`) are always logged at `Warning` under
`Piton.IPRestrictions.Endpoints.IPRestrictionsOptions`.

### Metrics

The middleware records metrics with `System.Diagnostics.Metrics`, on the `Piton.IPRestrictions` meter
(`IPRestrictionsMetrics.MeterName`). Each is a counter of requests to endpoints with IP restrictions, in `{request}`,
enabled by an option in `IPRestrictionsOptions.Metrics`:

| Instrument                               | Counts                                                         | Option            | Default |
|:-----------------------------------------|:---------------------------------------------------------------|:------------------|:--------|
| `piton.ip_restrictions.requests`         | All requests, including ones the endpoint's predicate exempted | `TotalRequests`   | Off     |
| `piton.ip_restrictions.allowed_requests` | Allowed requests                                               | `AllowedRequests` | Off     |
| `piton.ip_restrictions.denied_requests`  | Denied requests                                                | `DeniedRequests`  | On      |

To export them with [OpenTelemetry](https://opentelemetry.io/docs/languages/dotnet/), install the
`Piton.IPRestrictions.Endpoints.Telemetry` package:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddIPRestrictionsInstrumentation());
```

or manually without the package:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter(IPRestrictionsMetrics.MeterName));
```

#### Configure metrics

To leave requests out of the metrics, set `Filter`, which is called with the same endpoint, client IP address and
whether the request was denied, and returns whether to record the request.

To add tags to the metrics, set `ConfigureTags`, which is called with a reference to the tag list, the same endpoint,
client IP address and whether the request was denied.

```csharp
builder.Services.AddIPRestrictions(o =>
{
    // Exclude permitted health checks from all metrics
    o.Metrics.Filter = (endpoint, remoteIp, denied) => !denied && endpoint?.DisplayName != "Health checks";
    
    // Tag api vs non-api endpoints
    o.Metrics.ConfigureTags = (ref TagList tags, endpoint, remoteIp, denied) =>
        tags.Add("api", endpoint is RouteEndpoint { RoutePattern.RawText: { } route } && route.StartsWith("/api") ? "true" : "false");
});
```

Each distinct combination of tag values is a separate time series, so tags should have a low cardinality (few distinct values) 
to avoid high memory usage and performance issues.

`AddIPRestrictionsInstrumentation` can also configure the metrics options:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddIPRestrictionsInstrumentation(o =>
    {
        // Configure metrics options here
    }));
```

### Reverse Proxies

Parsing `X-Forwarded-For` is a security risk since many reverse proxies (like Cloudflare and Envoy) **append** 
(not override) the original client IP address to the client-supplied `X-Forwarded-For` header, allowing a client to 
spoof their IP address by adding a fake `X-Forwarded-For` header. However, reverse proxies also commonly put
the actual client IP in a different header. This library includes a function `ConfigureForReverseProxy` that configures
the correct header value for the known proxies below.

| Reverse Proxy                                                                                   | Header                     | Notes                                                                                                                                                                                                                                                               |
|-------------------------------------------------------------------------------------------------|----------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| [Envoy](https://www.envoyproxy.io/)                                                             | `X-Envoy-External-Address` | Envoy only sets this header when `use_remote_address` is `true` (i.e. it is the edge proxy). Used by Azure Container Apps (see [Envoy HTTP Headers](https://www.envoyproxy.io/docs/envoy/latest/configuration/http/http_conn_man/headers#x-envoy-external-address)) |
| [Cloudflare](https://developers.cloudflare.com/fundamentals)                                    | `CF-Connecting-IP`         | See [Cloudflare HTTP Headers](https://developers.cloudflare.com/fundamentals/reference/http-headers/#cf-connecting-ip)                                                                                                                                              |
| [Traefik](https://doc.traefik.io/)                                                              | `X-Real-IP`                | See [Traefik HTTP Headers](https://doc.traefik.io/traefik/reference/routing-configuration/http/middlewares/headers/#opt-Clients-IP)                                                                                                                                 |
| [YARP](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/getting-started) | `X-Forwarded-For`          | By default YARP removes any incoming `X-Forwarded-For` headers (see [YARP Forwarded Headers](https://learn.microsoft.com/aspnet/core/fundamentals/servers/yarp/header-guidelines#x-forwarded--forwarded))                                                           |
| [Caddy](https://caddyserver.com/)                                                               | `X-Forwarded-For`          | By default Caddy ignores incoming `X-Forwarded-*` values unless the request comes from one of its `trusted_proxies` (see [Caddy reverse_proxy defaults](https://caddyserver.com/docs/caddyfile/directives/reverse_proxy#defaults))                                  |
| [Azure Front Door](https://learn.microsoft.com/azure/frontdoor/front-door-overview)             | `X-Azure-SocketIP`         | See [Azure Front Door HTTP Headers](https://learn.microsoft.com/azure/frontdoor/front-door-http-headers-protocol#from-the-front-door-to-the-backend)                                                                                                                |

### Known Environments

Since .NET 8, forwarded headers are only applied when the request comes from a known proxy (see
[ForwardedHeadersOptions.KnownIPNetworks](https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.builder.forwardedheadersoptions.knownipnetworks)).
This library includes a helper function `AddKnownNetworkForCurrentEnvironment` that adds the correct value to `ForwardedHeadersOptions.KnownIPNetworks`
for certain known environments, detected by an environment variable:

| Environment                                                                       | Environment Variable                                                                                                                  | Known IP Networks                               | Notes                                                                                                                                                                                               |
|-----------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------|-------------------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| [Azure Container Apps](https://learn.microsoft.com/azure/container-apps/overview) | [`CONTAINER_APP_REPLICA_NAME`](https://learn.microsoft.com/azure/container-apps/environment-variables#built-in-environment-variables) | `100.64.0.0/10`                                 | The default RFC 6598 CG-NAT network. An environment in a custom virtual network may use a different network.                                                                                        |
| [Azure App Service](https://learn.microsoft.com/azure/app-service/overview)       | [`WEBSITE_SITE_NAME`](https://learn.microsoft.com/azure/app-service/reference-app-settings#app-environment)                           | `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16` | [Documented](https://learn.microsoft.com/azure/app-service/configure-language-dotnetcore#detect-https-session) for the App Service load balancer. Added both as IPv4 and IPv4-mapped IPv6 networks. |
| [Kubernetes](https://kubernetes.io/)                                              | [`KUBERNETES_SERVICE_HOST`](https://kubernetes.io/docs/concepts/services-networking/service/#environment-variables)                   | `10.0.0.0/8`                                    | The pod network depends on the cluster's configuration, so this may need to be changed.                                                                                                             |

# ASP.Net Core IP restrictions

[![CI](https://img.shields.io/github/actions/workflow/status/jeremy-morren/AspNetCore.IPRestrictions/draft.yml?branch=main&label=CI)](https://github.com/jeremy-morren/AspNetCore.IPRestrictions/actions/workflows/draft.yml?query=branch%3Amain)
[![AspNetCore.IPRestrictions.Endpoints](https://img.shields.io/nuget/v/AspNetCore.IPRestrictions.Endpoints?label=AspNetCore.IPRestrictions.Endpoints)](https://www.nuget.org/packages/AspNetCore.IPRestrictions.Endpoints)
[![AspNetCore.IPRestrictions.Endpoints.Telemetry](https://img.shields.io/nuget/v/AspNetCore.IPRestrictions.Endpoints.Telemetry?label=AspNetCore.IPRestrictions.Endpoints.Telemetry)](https://www.nuget.org/packages/AspNetCore.IPRestrictions.Endpoints.Telemetry)

Restricts endpoints to a list of addresses and networks. Opt in per endpoint, 
either with `[RestrictIP]` or `.RestrictIP()` on an endpoint convention builder, optionally with a
predicate deciding per request whether the restriction applies.

### Installation

Install the `AspNetCore.IPRestrictions.Endpoints` NuGet package: `dotnet add package AspNetCore.IPRestrictions.Endpoints`

```msbuild
<Project>
  <ItemGroup>
    <PackageReference Include="AspNetCore.IPRestrictions.Endpoints" Version="1.0.1" />
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

// ... other services

var app = builder.Build();

// When behind a reverse proxy forwarded headers must be enabled, 
// otherwise the remote address will always be the proxy's address.
// Since .NET 8, forwarded headers are only applied for known proxies: see section Forwarded Headers below
app.UseDefaultForwardedHeaders();

// ... other middleware

app.UseRouting();

// Map the middleware after UseRouting and before UseAuthentication
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
- `ConfigureForReverseProxy` - configures the forwarded headers options for a specific reverse proxy.
- `AddKnownIPNetwork` - adds the correct value to `ForwardedHeadersOptions.KnownIPNetworks` for some known environments.

```csharp
app.UseForwardedHeaders(new ForwardedHeadersOptions()
    {
        // Expose the original protocol and client IP address to the application
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    }
    .ConfigureForReverseProxy(ReverseProxy.YourProxy) // Configure for the specific reverse proxy you are using
    .AddKnownIPNetwork(); // Add the correct value to ForwardedHeadersOptions.KnownIPNetworks for the current environment
```

### Metrics

The middleware records metrics with `System.Diagnostics.Metrics`, on the `AspNetCore.IPRestrictions` meter
(`IPRestrictionsMetrics.MeterName`). Each is a counter of requests to endpoints with IP restrictions, in `{request}`,
enabled by an option in `IPRestrictionsOptions.Metrics`:

| Instrument | Counts | Option | Default |
| :--- | :--- | :--- | :--- |
| `aspnetcore.ip_restrictions.requests` | All requests, including ones the endpoint's predicate exempted | `TotalRequests` | Off |
| `aspnetcore.ip_restrictions.allowed_requests` | Allowed requests | `AllowedRequests` | Off |
| `aspnetcore.ip_restrictions.denied_requests` | Denied requests | `DeniedRequests` | On |

```json
{
  "IPRestrictions": {
    "Metrics": {
      "TotalRequests": true,
      "AllowedRequests": true,
      "DeniedRequests": true
    }
  }
}
```

Measurements have no tags by default. To add them, set `ConfigureTags`, which is called with the endpoint and the
client's IP address (an IPv4 client on a dual-mode socket, `::ffff:a.b.c.d`, is given as the IPv4 address).
For example, to tag with the route and the client IP, using the OpenTelemetry semantic conventions:

```csharp
builder.Services.AddIPRestrictions(o =>
{
    o.Metrics.ConfigureTags = (ref TagList tags, Endpoint endpoint, IPAddress? remoteIp) =>
    {
        if (endpoint is RouteEndpoint { RoutePattern.RawText: { } route })
            tags.Add("http.route", route);
        if (remoteIp != null)
            tags.Add("client.address", remoteIp.ToString());
    };
});
```

Each distinct combination of tag values is a separate time series. Tagging with the client IP makes their number
unbounded: the metrics grow with the number of clients (for denied requests, with the number of addresses
scanning the application).

To export them with [OpenTelemetry](https://opentelemetry.io/docs/languages/dotnet/), install the
`AspNetCore.IPRestrictions.Endpoints.Telemetry` package:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddIPRestrictionsInstrumentation());
```

`AddIPRestrictionsInstrumentation` can also configure the metrics options:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddIPRestrictionsInstrumentation(o =>
    {
        o.AllowedRequests = true;
        o.ConfigureTags = (ref TagList tags, Endpoint endpoint, IPAddress? remoteIp) =>
        {
            if (endpoint is RouteEndpoint { RoutePattern.RawText: { } route })
                tags.Add("http.route", route);
        };
    }));
```

or manually without the package:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter(IPRestrictionsMetrics.MeterName));
```
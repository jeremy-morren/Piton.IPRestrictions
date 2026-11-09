# ASP.Net Core IP restrictions

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

When behind a reverse proxy, the remote address will always be the proxy's address unless forwarded headers are enabled. Since .NET 8, forwarded headers are only applied for known proxies. If your reverse proxy is not in the list of known proxies, you must configure it:

```csharp

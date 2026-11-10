using System.Net;
using Piton.IPRestrictions.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
#if NET11_0_OR_GREATER
using IPNetwork = System.Net.IPNetwork;
#else
using IPNetwork = Piton.IPRestrictions.Endpoints.IPNetwork;
#endif

namespace Piton.IPRestrictions.Tests;

public class IPRestrictionsMiddlewareTests
{
    [Fact]
    public async Task AllowLoopback()
    {
        await using var app = new TestWebApp(
            testPort: 1,
            configureOptions: o =>
            {
                o.AllowLoopbackIPs = true;
            },
            useForwardedHeaders: true);

        // Loopback should work without X-Forwarded-For header
        IPAddress.IsLoopback(await app.GetRemoteIP()).Should().BeTrue();
        
        await Assert.AllAsync([
                "127.0.0.1",
                "127.0.2.255", // A different permitted loopback IPv4 address
                "::1"
            ],
            async ip =>
            {
                //Should work with  X-Forwarded-For header
                (await app.GetRemoteIP(xForwardedFor: ip)).Should().Be(IPAddress.Parse(ip));
            });

        // Non-Loopback IPs should be blocked
        await app.EnsureFailure(xForwardedFor: "4.3.2.1");
        await app.EnsureFailure(xForwardedFor: "::ffff:4.3.2.1");
        await app.EnsureFailure(xForwardedFor: "10.1.2.3");
        await app.EnsureFailure(xForwardedFor: "::ffff:192.168.2.6");
    }

    [Fact]
    public async Task AllowForwardedHeaders()
    {
        await using var app = new TestWebApp(
            testPort: 2,
            configureOptions: o =>
            {
                o.AllowLoopbackIPs = false;
                
                o.AllowedIPs.Add("1.2.3.4");
                o.AllowedIPs.Add("10.9.8.0/28");
                o.AllowedIPs.Add("::a");
                o.AllowedIPs.Add("::c/127");
            },
            useForwardedHeaders: true);

        // Match exact IP
        (await app.GetRemoteIP("1.2.3.4")).Should().Be(IPAddress.Parse("1.2.3.4"));
        // Match in subnet
        (await app.GetRemoteIP("10.9.8.1")).Should().Be(IPAddress.Parse("10.9.8.1"));

        // Match exact IPv6
        (await app.GetRemoteIP("::a")).Should().Be(IPAddress.Parse("::a"));
        // Match in IPv6 subnet
        (await app.GetRemoteIP("::d")).Should().Be(IPAddress.Parse("::d"));

        await app.EnsureFailure(xForwardedFor: "4.3.2.1");
        await app.EnsureFailure(xForwardedFor: "fe80::1");
        await app.EnsureFailure(xForwardedFor: "10.9.100.1"); // Not in 10.9.8.0/28
        await app.EnsureFailure(xForwardedFor: "::e"); // Not in ::c/127

        await app.EnsureFailure(); // No X-Forwarded-For, so remote IP is loopback which is not allowed
    }

    [Fact]
    public async Task AttributeOnEndpointShouldBeRespected()
    {
        await using var app = new TestWebApp(
            testPort: 4,
            configureOptions: o => o.AllowLoopbackIPs = false,
            configureApp: app =>
            {
                app.MapGet("/public", () => "Hello World");
                app.MapGet("/restricted", () => "Restricted").RestrictIP();
                app.MapGet("/attribute", [RestrictIP] () => "Attribute");
            });

        (await app.InvokeGet("/public")).Should().Be("Hello World");

        // Loopback is not allowed, so these should fail
        await app.EnsureFailure("/restricted");
        await app.EnsureFailure("/attribute");
        await app.EnsureFailure();
    }

    [Fact]
    public async Task PredicateShouldBeRespected()
    {
        const string headerName = "AllowIP";
        const string ip = "6.5.4.3";

        await using var app = new TestWebApp(
            testPort: 5,
            configureOptions: o =>
            {
                o.AllowLoopbackIPs = false;
                o.AllowedIPs.Add(ip);
            },
            useForwardedHeaders: true,
            configureApp: app =>
            {
                app.MapGet("/public", () => "Hello World");
                app.MapGet("/restricted", () => "Restricted")
                    .RestrictIP(context => context.Request.Headers[headerName] != "true");
            });

        (await app.InvokeGet("/public")).Should().Be("Hello World");
        (await app.InvokeGet("/restricted", ip)).Should().Be("Restricted");

        // Without header, IP restriction should apply and block access
        await app.EnsureFailure("/restricted");

        // With header, should be allowed (i.e. predicate returns true)
        (await app.InvokeGet("/restricted",
                headers: new Dictionary<string, string> { { headerName, "true" } }))
            .Should().Be("Restricted");
    }

    [Fact]
    public async Task EndpointBuilderPredicateShouldBeRespected()
    {
        const string headerName = "AllowIP";
        const string ip = "6.5.4.3";

        await using var app = new TestWebApp(
            testPort: 70,
            configureOptions: o =>
            {
                o.AllowLoopbackIPs = false;
                o.AllowedIPs.Add(ip);
            },
            useForwardedHeaders: true,
            configureApp: app =>
            {
                app.MapGet("/public", () => "Hello World");
                app.MapGet("/restricted", () => "Restricted")
                    .Add(endpoint => endpoint.RestrictIP(context => context.Request.Headers[headerName] != "true"));
            });

        (await app.InvokeGet("/public")).Should().Be("Hello World");
        (await app.InvokeGet("/restricted", ip)).Should().Be("Restricted");

        // Without header, IP restriction should apply and block access
        await app.EnsureFailure("/restricted");

        // With header, should be allowed (i.e. predicate returns false, so the restriction doesn't apply)
        (await app.InvokeGet("/restricted",
                headers: new Dictionary<string, string> { { headerName, "true" } }))
            .Should().Be("Restricted");
    }

    [Fact]
    public async Task EndpointBuilderRestrictIPShouldBeRespected()
    {
        const string ip = "6.5.4.3";

        await using var app = new TestWebApp(
            testPort: 71,
            configureOptions: o =>
            {
                o.AllowLoopbackIPs = false;
                o.AllowedIPs.Add(ip);
            },
            useForwardedHeaders: true,
            configureApp: app =>
            {
                app.MapGet("/public", () => "Hello World");
                app.MapGet("/restricted", () => "Restricted").Add(endpoint => endpoint.RestrictIP());
            });

        (await app.InvokeGet("/public")).Should().Be("Hello World");
        (await app.InvokeGet("/restricted", ip)).Should().Be("Restricted");

        // Loopback is not allowed
        await app.EnsureFailure("/restricted");
    }

    [Fact]
    public void EndpointBuilderRestrictIPShouldAddTheAttribute()
    {
        var endpointBuilder = CreateEndpointBuilder();

        endpointBuilder.RestrictIP().Should().BeSameAs(endpointBuilder);

        endpointBuilder.Metadata.OfType<RestrictIPAttribute>().Should().ContainSingle()
            .Which.Predicate.Should().BeNull();
    }

    [Fact]
    public void EndpointBuilderRestrictIPShouldAddThePredicate()
    {
        var endpointBuilder = CreateEndpointBuilder();
        Predicate<HttpContext> predicate = _ => true;

        endpointBuilder.RestrictIP(predicate).Should().BeSameAs(endpointBuilder);

        endpointBuilder.Metadata.OfType<RestrictIPAttribute>().Should().ContainSingle()
            .Which.Predicate.Should().BeSameAs(predicate);
    }

    [Fact]
    public void EndpointBuilderRestrictIPShouldRejectNullArguments()
    {
        var withoutBuilderOrPredicate = () => ((EndpointBuilder)null!).RestrictIP();
        withoutBuilderOrPredicate.Should().Throw<ArgumentNullException>().WithParameterName("endpointBuilder");

        var withoutBuilder = () => ((EndpointBuilder)null!).RestrictIP(_ => true);
        withoutBuilder.Should().Throw<ArgumentNullException>().WithParameterName("endpointBuilder");

        var withoutPredicate = () => CreateEndpointBuilder().RestrictIP(null!);
        withoutPredicate.Should().Throw<ArgumentNullException>().WithParameterName("predicate");
    }

    private static RouteEndpointBuilder CreateEndpointBuilder() =>
        new(_ => Task.CompletedTask, RoutePatternFactory.Parse("/"), order: 0);

    [Fact]
    public async Task AllowLanIPsShouldBeRespected()
    {
        await using var app = new TestWebApp(
            testPort: 6,
            configureOptions: o =>
            {
                o.AllowLanIPs = true;
            },
            useForwardedHeaders: true);

        await Assert.AllAsync([
                "10.0.1.2",
                "::ffff:10.42.0.158",
                "172.16.2.3",
                "::ffff:172.16.1.2",
                "192.168.1.1",
                "::ffff:192.168.2.1"
            ],
            async ip =>
            {
                // LAN ip should be allowed
                (await app.GetRemoteIP(ip)).Should().Be(IPAddress.Parse(ip));
            });

        // Non-LAN IPs should be blocked
        await app.EnsureFailure(xForwardedFor: "4.3.2.1");
        await app.EnsureFailure(xForwardedFor: "::ffff:4.3.2.1");
    }

    [Theory]
    [InlineData(11, null, (IPRestrictionMode)0)]
    [InlineData(12, "Remote", IPRestrictionMode.Enabled)]
    public async Task AddRemoteEndpointHeaderShouldModifyResponse(int testPort, string? headerName, IPRestrictionMode mode)
    {
        await using var app = new TestWebApp(
            testPort: testPort,
            configureOptions: o =>
            {
                if (headerName != null)
                    o.RemoteEndpointHeaderName = headerName;
                o.Mode = IPRestrictionMode.AddEndpointHeader | mode;
                o.AllowedIPs.Add("0.0.0.0/0");
            },
            useForwardedHeaders: true);
        
        var expectedHeaderName = headerName ?? "X-Remote-Endpoint";

        var client = app.CreateClient();
        await Assert.AllAsync(
            ["5.4.3.2", "::ffff:1.3.5.7"],
            async ip =>
            {
                var request = app.CreateRequest("/", ip);
                using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                response.Headers.TryGetValues(expectedHeaderName, out var values).Should().BeTrue();
                values.Should().ContainSingle().Which.Should().Be(ip);
            });
    }

    /// <summary>
    /// <see cref="IPRestrictionMode.Disabled"/> short-circuits the middleware, whatever else is set.
    /// Otherwise <see cref="IPRestrictionMode.AddEndpointHeader"/> and <see cref="IPRestrictionMode.Enabled"/>
    /// each have their own effect, independently of the other.
    /// </summary>
    [Theory]
    [InlineData((IPRestrictionMode)0)]
    [InlineData(IPRestrictionMode.Disabled)]
    [InlineData(IPRestrictionMode.AddEndpointHeader)]
    [InlineData(IPRestrictionMode.Disabled | IPRestrictionMode.AddEndpointHeader)]
    [InlineData(IPRestrictionMode.Enabled)]
    [InlineData(IPRestrictionMode.Disabled | IPRestrictionMode.Enabled)]
    [InlineData(IPRestrictionMode.AddEndpointHeader | IPRestrictionMode.Enabled)]
    [InlineData(IPRestrictionMode.Disabled | IPRestrictionMode.AddEndpointHeader | IPRestrictionMode.Enabled)]
    public async Task ModeFlagsShouldBeAppliedIndependentlyUnlessDisabled(IPRestrictionMode mode)
    {
        const string allowedIP = "203.0.113.5";
        const string deniedIP = "198.51.100.1";

        await using var app = new TestWebApp(
            // A port per combination of the flags (0-7)
            testPort: 80 + (int)mode,
            configureOptions: o =>
            {
                o.Mode = mode;
                o.AllowLoopbackIPs = false;
                o.AllowedIPs.Add("203.0.113.0/24");
            },
            useForwardedHeaders: true);

        var disabled = mode.HasFlag(IPRestrictionMode.Disabled);
        var expectHeader = !disabled && mode.HasFlag(IPRestrictionMode.AddEndpointHeader);
        var expectRestricted = !disabled && mode.HasFlag(IPRestrictionMode.Enabled);

        var client = app.CreateClient();
        foreach (var ip in new[] { allowedIP, deniedIP })
        {
            using var response = await client.SendAsync(app.CreateRequest("/", ip), TestContext.Current.CancellationToken);

            var expectDenied = expectRestricted && ip == deniedIP;
            response.StatusCode.Should().Be(expectDenied ? HttpStatusCode.Forbidden : HttpStatusCode.OK,
                "mode {0}, IP {1}", mode, ip);

            if (expectHeader)
                response.Headers.GetValues("X-Remote-Endpoint").Should().ContainSingle().Which.Should().Be(ip);
            else
                response.Headers.Contains("X-Remote-Endpoint").Should().BeFalse("mode {0}", mode);
        }
    }

    [Theory]
    [InlineData(20, (IPRestrictionMode)0)]
    [InlineData(21, IPRestrictionMode.AddEndpointHeader)]
    public async Task DisableMiddlewareShouldBeRespected(int testPort, IPRestrictionMode mode)
    {
        await using var app = new TestWebApp(
            testPort: testPort,
            configureOptions: o =>
            {
                o.Mode = IPRestrictionMode.Disabled | mode;
            },
            useForwardedHeaders: true);

        var client = app.CreateClient();
        await Assert.AllAsync(
            ["5.4.3.2", "::ffff:1.3.5.7"],
            async ip =>
            {
                var request = app.CreateRequest("/", ip);
                using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                response.Headers.Contains("X-Remote-Endpoint").Should().BeFalse();
            });
    }

    [Fact]
    public async Task MiddlewareShouldIgnoreMissingEndpoint()
    {
        await using var app = new TestWebApp(
            testPort: 25,
            configureOptions: _ => {},
            useForwardedHeaders: false);

        var client = app.CreateClient();
        var request = app.CreateRequest("/nonexistent");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public void BindConfigurationShouldSucceed()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "IPRestrictions:AllowLoopbackIPs", "false" },
                { "IPRestrictions:AllowedIPs:0", "1.2.3.4" },
                { "IPRestrictions:AllowedIPs:1", "::1" },
                { "IPRestrictions:AllowedIPs:2", "172.16.0.0/12" },
                { "IPRestrictions:AllowedIPs:3", "::ffff:172.16.0.0/108" }
            })
            .Build();
        var options = new IPRestrictionsOptions();
        configuration.Bind("IPRestrictions", options);
        options.AllowedIPs.Count.Should().Be(4);
        options.AllowedIPs[0].Should().Be("1.2.3.4");
        options.AllowedIPs[1].Should().Be("::1");
        options.AllowedIPs[2].Should().Be("172.16.0.0/12");
        options.AllowedIPs[3].Should().Be("::ffff:172.16.0.0/108");
        options.ParseIPAddresses(null);
        options.AllowedIPAddresses.Should().HaveCount(2)
            .And.BeEquivalentTo(options.AllowedIPs.Take(2).Select(IPAddress.Parse));
        options.AllowedIPNetworks.Should().HaveCount(2)
            .And.BeEquivalentTo(options.AllowedIPs.Skip(2).Select(IPNetwork.Parse));
    }

    [Fact]
    public void RegisterMiddlewareInOrderShouldNotThrow()
    {
        var builder = WebApplication.CreateBuilder();

        builder.Services.AddIPRestrictions();
        builder.Services.AddAuthorization();

        var app = builder.Build();

        // After routing, before authentication and authorization
        app.UseRouting();

        Action registerMiddleware = () => app.UseIPRestrictions();
        registerMiddleware.Should().NotThrow();

        app.UseAuthentication();
        app.UseAuthorization();
    }

    // UseIPRestrictions can only check the middleware order from .NET 7
#if NET7_0_OR_GREATER
    [Fact]
    public void RegisterMiddlewareBeforeRoutingShouldThrow()
    {
        var builder = WebApplication.CreateBuilder();

        builder.Services.AddIPRestrictions();

        var app = builder.Build();

        Action registerMiddleware = () => app.UseIPRestrictions();
        registerMiddleware.Should().Throw<InvalidOperationException>()
            .WithMessage("The IPRestrictionsMiddleware must be added after the routing middleware");

        app.UseRouting();

        app.UseIPRestrictions(); // Should not throw now
    }

#endif

    [Fact]
    public void RegisterMiddlewareAfterAuthenticationAndAuthorizationShouldNotThrow()
    {
        var builder = WebApplication.CreateBuilder();

        builder.Services.AddIPRestrictions();
        builder.Services.AddAuthorization();

        var app = builder.Build();

        // Anywhere after routing
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        Action registerMiddleware = () => app.UseIPRestrictions();
        registerMiddleware.Should().NotThrow();
    }

    [Theory]
    [InlineData("127.0.0.1", "127.0.0.1")]
    [InlineData("fe80::1", "fe80::1")]
    [InlineData("127.0.0.1", "127.0.0.1/32")]
    [InlineData("::ffff:172.18.1.5", "::ffff:172.16.0.0/108")]
    [InlineData("172.18.1.5", "172.16.0.0/12")]
    [InlineData("192.168.1.100", "192.168.1.0/24")]
    [InlineData("::ffff:192.168.1.100", "::ffff:192.168.1.0/120")]
    [InlineData("2001:db8::1", "2001:db8::/32")]
    [InlineData("fe80::abcd", "fe80::/12")]
    [InlineData("10.0.0.5", "10.0.0.0/8")]
    [InlineData("::1", "::1/128")]
    // An IPv4 client arrives as ::ffff:a.b.c.d when Kestrel listens on a dual mode socket,
    // which is the default. A rule written the plain way has to match it either way it is
    // spelled, as a single address or as a network.
    [InlineData("::ffff:203.0.113.5", "203.0.113.5")]
    [InlineData("::ffff:203.0.113.5", "203.0.113.5/32")]
    [InlineData("::ffff:192.168.1.100", "192.168.1.0/24")]
    [InlineData("203.0.113.5", "::ffff:203.0.113.5")]
    // An IPv6 network compares IPv4-mapped addresses as IPv6 (wrong in .NET 9 and 10, dotnet/runtime#131861)
    [InlineData("::ffff:1.2.3.4", "::ffff:0.0.0.0/96")]
    [InlineData("::ffff:1.2.3.4", "::/8")]
    public void ValidRemoteIPShouldBeAllowed(string ip, params string[] allowedIPs)
    {
        var options = new IPRestrictionsOptions()
        {
            AllowedIPs = allowedIPs.ToList()
        };
        options.ParseIPAddresses(null);
        IPRestrictionsMiddleware.IsAllowedIP(
            options,
            IPAddress.Parse(ip),
            out var matchedAddress,
            out var matchedNetwork).Should().BeTrue();
        var remote = IPAddress.Parse(ip);

        if (matchedAddress != null)
            // The same host, not necessarily the same spelling: an allowed 203.0.113.5
            // matches a client arriving as ::ffff:203.0.113.5
            Unmap(matchedAddress).Should().Be(Unmap(remote));
        else
            matchedNetwork.Should().NotBeNull().And.Match((IPNetwork network) => network.Contains(remote));
    }

    [Theory]
    [InlineData("127.0.0.1", "127.0.0.5", "128.0.0.1/32", "fe80::1/64")]
    [InlineData("fe80::1", "127.0.0.1", "128.0.0.1/32")]
    [InlineData("::ffff:100.8.8.2", "172.16.0.0/12", "::ffff:172.16.0.0/108")]
    [InlineData("100.5.4.2", "172.16.0.0/12", "::ffff:172.16.0.0/108")]
    [InlineData("192.168.2.100", "192.168.1.0/24")]
    [InlineData("::ffff:192.168.2.100", "::ffff:192.168.1.0/120")]
    [InlineData("2001:db9::1", "2001:db8::/32")]
    [InlineData("fe90::abcd", "fe80::/48", "fe90::abc")]
    [InlineData("11.0.0.5", "10.0.0.0/8")]
    [InlineData("::2", "::1/128")]
    // Unmapping must not make a different host match
    [InlineData("::ffff:203.0.113.6", "203.0.113.5")]
    [InlineData("203.0.113.6", "::ffff:203.0.113.5")]
    // IPv4-mapped addresses must not match IPv6 networks by their IPv4 part (true in .NET 9 and 10, dotnet/runtime#131861)
    [InlineData("::ffff:0.0.0.1", "::1/128")]
    [InlineData("::ffff:0:0", "fd00::/8")]
    public void InvalidRemoteIPShouldNotBeAllowed(string ip, params string[] allowedIPs)
    {
        var options = new IPRestrictionsOptions()
        {
            AllowedIPs = allowedIPs.ToList(),
            AllowLoopbackIPs = false
        };
        options.ParseIPAddresses(null);
        IPRestrictionsMiddleware.IsAllowedIP(options, IPAddress.Parse(ip), out _, out _).Should().BeFalse();
    }

    private static IPAddress Unmap(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
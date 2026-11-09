using System.Net;
using AspNetCore.IPRestrictions.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace AspNetCore.IPRestrictions.Tests;

public class ReverseProxyTests
{
    [Theory]
    [InlineData(ReverseProxy.Envoy, "x-envoy-external-address")]
    [InlineData(ReverseProxy.Cloudflare, "cf-connecting-ip")]
    [InlineData(ReverseProxy.Traefik, "X-Real-Ip")]
    [InlineData(ReverseProxy.YARP, "X-Forwarded-For")]
    [InlineData(ReverseProxy.Caddy, "X-Forwarded-For")]
    [InlineData(ReverseProxy.AzureFrontDoor, "X-Azure-SocketIP")]
    public void ConfigureForReverseProxyShouldSetHeaderName(ReverseProxy reverseProxy, string expectedHeaderName)
    {
        var options = new ForwardedHeadersOptions();

        options.ConfigureForReverseProxy(reverseProxy).Should().BeSameAs(options);

        options.ForwardedForHeaderName.Should().Be(expectedHeaderName);
    }

    [Fact]
    public void ConfigureForReverseProxyShouldMapEveryReverseProxy()
    {
        foreach (var reverseProxy in Enum.GetValues<ReverseProxy>())
        {
            var configure = () => new ForwardedHeadersOptions().ConfigureForReverseProxy(reverseProxy);
            configure.Should().NotThrow($"{reverseProxy} should be mapped to a header");
        }
    }

    [Fact]
    public void ConfigureForReverseProxyShouldRejectInvalidValues()
    {
        var configure = () => new ForwardedHeadersOptions().ConfigureForReverseProxy((ReverseProxy)0);
        configure.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("reverseProxy");

        var nullOptions = () => ((ForwardedHeadersOptions)null!).ConfigureForReverseProxy(ReverseProxy.Envoy);
        nullOptions.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Theory]
    [InlineData(ReverseProxy.Envoy)]
    [InlineData(ReverseProxy.Cloudflare)]
    [InlineData(ReverseProxy.Traefik)]
    [InlineData(ReverseProxy.YARP)]
    [InlineData(ReverseProxy.Caddy)]
    [InlineData(ReverseProxy.AzureFrontDoor)]
    public async Task ForwardedHeadersMiddlewareShouldReadClientIPFromReverseProxyHeader(ReverseProxy reverseProxy)
    {
        const string clientIP = "203.0.113.9";

        // Loopback is a known proxy by default
        var options = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor }
            .ConfigureForReverseProxy(reverseProxy);

        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Headers[options.ForwardedForHeaderName] = clientIP;

        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance, Options.Create(options));
        await middleware.Invoke(context);

        context.Connection.RemoteIpAddress.Should().Be(IPAddress.Parse(clientIP));
    }

    [Theory]
    [InlineData(ReverseProxy.Envoy)]
    [InlineData(ReverseProxy.Cloudflare)]
    [InlineData(ReverseProxy.Traefik)]
    [InlineData(ReverseProxy.YARP)]
    [InlineData(ReverseProxy.Caddy)]
    [InlineData(ReverseProxy.AzureFrontDoor)]
    public async Task ForwardedHeadersMiddlewareShouldIgnoreSpoofedValuesBeforeTheProxyValue(ReverseProxy reverseProxy)
    {
        const string spoofedIP = "198.51.100.66";
        const string clientIP = "203.0.113.9";

        // Loopback is a known proxy by default
        var options = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor }
            .ConfigureForReverseProxy(reverseProxy);

        // If a proxy appends its value instead of overwriting a client-sent header,
        // the client's value comes first: either in the same header, or as an earlier header line
        foreach (var headerValues in new StringValues[] { $"{spoofedIP}, {clientIP}", new[] { spoofedIP, clientIP } })
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            context.Request.Headers[options.ForwardedForHeaderName] = headerValues;

            var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance, Options.Create(options));
            await middleware.Invoke(context);

            // The default ForwardLimit of 1 only uses the last (rightmost) value, which the proxy added
            context.Connection.RemoteIpAddress.Should().Be(IPAddress.Parse(clientIP), $"header values: {headerValues}");
        }
    }
}

using System.Net;
using Piton.IPRestrictions.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Piton.IPRestrictions.Tests;

/// <summary>
/// Tests for <see cref="ForwardedHeadersOptionsIPRestrictionsExtensions.AddKnownIPNetwork(ForwardedHeadersOptions)"/>.
/// The environment is passed in rather than set on the process, since tests run in parallel.
/// </summary>
public class KnownIPNetworkTests
{
    private static readonly string[] AppServiceNetworks =
    [
        "::ffff:10.0.0.0/104", "::ffff:192.168.0.0/112", "::ffff:172.16.0.0/108",
        "10.0.0.0/8", "192.168.0.0/16", "172.16.0.0/12"
    ];

    [Fact]
    public void AddKnownIPNetworkShouldAddNothingOutsideAKnownEnvironment()
    {
        AddedNetworks(new Dictionary<string, string>()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("CONTAINER_APP_REPLICA_NAME", "100.64.0.0/10")]
    [InlineData("KUBERNETES_SERVICE_HOST", "10.0.0.0/8")]
    public void AddKnownIPNetworkShouldAddTheEnvironmentNetwork(string variable, string expectedNetwork)
    {
        AddedNetworks(new Dictionary<string, string> { [variable] = "value" })
            .Should().Equal(expectedNetwork);
    }

    [Fact]
    public void AddKnownIPNetworkShouldAddAppServiceNetworks()
    {
        AddedNetworks(new Dictionary<string, string> { ["WEBSITE_SITE_NAME"] = "my-app" })
            .Should().BeEquivalentTo(AppServiceNetworks);
    }

    [Fact]
    public void AddKnownIPNetworkShouldIgnoreEmptyVariables()
    {
        AddedNetworks(new Dictionary<string, string>
        {
            ["CONTAINER_APP_REPLICA_NAME"] = "",
            ["WEBSITE_SITE_NAME"] = "",
            ["KUBERNETES_SERVICE_HOST"] = ""
        }).Should().BeEmpty();
    }

    [Fact]
    public void AddKnownIPNetworkShouldPreferTheMostSpecificEnvironment()
    {
        // Azure Container Apps runs on Kubernetes, so both may be set
        AddedNetworks(new Dictionary<string, string>
        {
            ["CONTAINER_APP_REPLICA_NAME"] = "replica",
            ["KUBERNETES_SERVICE_HOST"] = "10.0.0.1"
        }).Should().Equal("100.64.0.0/10");

        AddedNetworks(new Dictionary<string, string>
        {
            ["WEBSITE_SITE_NAME"] = "my-app",
            ["KUBERNETES_SERVICE_HOST"] = "10.0.0.1"
        }).Should().BeEquivalentTo(AppServiceNetworks);
    }

    [Fact]
    public void AddKnownIPNetworkShouldKeepExistingNetworksAndReturnTheOptions()
    {
        var options = new ForwardedHeadersOptions();
        var before = Networks(options);

        options.AddKnownIPNetwork(Environment(new Dictionary<string, string> { ["KUBERNETES_SERVICE_HOST"] = "10.0.0.1" }))
            .Should().BeSameAs(options);

        Networks(options).Should().StartWith(before);
    }

    [Fact]
    public void AddKnownIPNetworkShouldRejectNullOptions()
    {
        var add = () => ((ForwardedHeadersOptions)null!).AddKnownIPNetwork(_ => null);
        add.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Theory]
    [InlineData("10.1.2.3", true)]
    [InlineData("::ffff:10.1.2.3", true)] // Kestrel's dual mode socket
    [InlineData("172.20.0.4", true)]
    [InlineData("::ffff:192.168.5.6", true)]
    [InlineData("203.0.113.1", false)]
    [InlineData("::ffff:203.0.113.1", false)]
    public async Task AppServiceLoadBalancerShouldBeTrustedForForwardedHeaders(string proxyIP, bool trusted)
    {
        const string clientIP = "198.51.100.7";

        var options = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor }
            .AddKnownIPNetwork(Environment(new Dictionary<string, string> { ["WEBSITE_SITE_NAME"] = "my-app" }));

        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(proxyIP);
        context.Request.Headers["X-Forwarded-For"] = clientIP;

        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance, Options.Create(options));
        await middleware.Invoke(context);

        context.Connection.RemoteIpAddress.Should().Be(IPAddress.Parse(trusted ? clientIP : proxyIP));
    }

    private static Func<string, string?> Environment(Dictionary<string, string> variables) =>
        name => variables.GetValueOrDefault(name);

    /// <summary>
    /// The networks <c>AddKnownIPNetwork</c> adds to new options, in CIDR notation.
    /// </summary>
    private static List<string> AddedNetworks(Dictionary<string, string> variables)
    {
        var options = new ForwardedHeadersOptions();
        var before = Networks(options);
        options.AddKnownIPNetwork(Environment(variables));
        return Networks(options).Skip(before.Count).ToList();
    }

    private static List<string> Networks(ForwardedHeadersOptions options) =>
#if NET10_0_OR_GREATER
        options.KnownIPNetworks.Select(n => $"{n.BaseAddress}/{n.PrefixLength}").ToList();
#else
        options.KnownNetworks.Select(n => $"{n.Prefix}/{n.PrefixLength}").ToList();
#endif
}

using Piton.IPRestrictions.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Piton.IPRestrictions.Tests;

/// <summary>
/// IP restrictions applied to MVC controllers, either with <see cref="RestrictIPAttribute"/>
/// or with the endpoint convention on <c>MapControllers()</c>.
/// </summary>
public class RestrictIPControllerTests
{
    private const string AllowedIP = "6.5.4.3";
    private const string PredicateHeader = "Apply-IP-Restrictions";

    private static TestWebApp CreateApp(int testPort, Action<ControllerActionEndpointConventionBuilder>? configureControllers = null) =>
        new(testPort: testPort,
            configureOptions: o =>
            {
                o.AllowLoopbackIPs = false;
                o.AllowedIPs.Add(AllowedIP);
            },
            useForwardedHeaders: true,
            configureServices: services => services
                .AddControllers()
                // The entry assembly is the test host, so controllers in this assembly must be added explicitly
                .AddApplicationPart(typeof(RestrictIPControllerTests).Assembly),
            configureApp: app =>
            {
                var controllers = app.MapControllers();
                configureControllers?.Invoke(controllers);
            });

    [Fact]
    public async Task AttributeOnControllerShouldRestrictAllActions()
    {
        await using var app = CreateApp(testPort: 30);

        foreach (var path in new[] { "/restricted-controller", "/restricted-controller/other" })
        {
            await app.EnsureFailure(path);
            (await app.InvokeGet(path, AllowedIP)).Should().Be(path);
        }
    }

    [Fact]
    public async Task AttributeOnActionShouldRestrictOnlyThatAction()
    {
        await using var app = CreateApp(testPort: 31);

        (await app.InvokeGet("/mixed/public")).Should().Be("/mixed/public");

        await app.EnsureFailure("/mixed/restricted");
        (await app.InvokeGet("/mixed/restricted", AllowedIP)).Should().Be("/mixed/restricted");
    }

    [Fact]
    public async Task PredicateOnControllerActionsShouldBeRespected()
    {
        await using var app = CreateApp(testPort: 32,
            controllers => controllers.RestrictIP(context => context.Request.Headers[PredicateHeader] == "true"));

        var applyRestrictions = new Dictionary<string, string> { { PredicateHeader, "true" } };

        // An action without the attribute, one with it on the action, and one with it on the controller:
        // the convention's predicate decides in every case
        foreach (var path in new[] { "/mixed/public", "/mixed/restricted", "/restricted-controller" })
        {
            // Predicate returns false: restrictions are skipped
            (await app.InvokeGet(path)).Should().Be(path);

            // Predicate returns true: restrictions apply
            await app.EnsureFailure(path, headers: applyRestrictions);
            (await app.InvokeGet(path, AllowedIP, applyRestrictions)).Should().Be(path);
        }
    }
}

[RestrictIP]
[Route("restricted-controller")]
public class RestrictedController : ControllerBase
{
    [HttpGet]
    public string Get() => Request.Path;

    [HttpGet("other")]
    public string Other() => Request.Path;
}

[Route("mixed")]
public class MixedController : ControllerBase
{
    [HttpGet("public")]
    public string Public() => Request.Path;

    [RestrictIP]
    [HttpGet("restricted")]
    public string Restricted() => Request.Path;
}

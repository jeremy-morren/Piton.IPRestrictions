using System.Net;
using AspNetCore.IPRestrictions.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AspNetCore.IPRestrictions.Tests;

/// <summary>
/// A test web application that can be used to test IP restrictions.
/// </summary>
public class TestWebApp : IAsyncDisposable
{
    private readonly int _port;
    private readonly WebApplication _app;
    private bool _started;

    public TestWebApp(
        int testPort,
        Action<IPRestrictionsOptions> configureOptions,
        bool useForwardedHeaders = false,
        Action<WebApplication>? configureApp = null,
        Action<IServiceCollection>? configureServices = null)
    {
        _port = GetPortRoot() + testPort;
        
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions()
            {
                EnvironmentName = Environments.Development
            });

        builder.WebHost.ConfigureKestrel(o =>
        {
            o.ListenLocalhost(_port);
        });

        builder.Services.AddHttpClient();

        builder.Services.AddIPRestrictions(configureOptions);

        builder.Services.AddHealthChecks();

        configureServices?.Invoke(builder.Services);

        _app = builder.Build();

        if (useForwardedHeaders)
            _app.UseForwardedHeaders(new ForwardedHeadersOptions()
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor,
#if NET10_0_OR_GREATER
                // KnownIPNetworks (System.Net.IPNetwork) replaced KnownNetworks in .NET 10
                KnownIPNetworks =
                {
                    System.Net.IPNetwork.Parse("127.0.0.1/32"),
                    System.Net.IPNetwork.Parse("::1/128"),
                },
#else
                KnownNetworks =
                {
                    new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Loopback, 32),
                    new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.IPv6Loopback, 128),
                },
#endif
            });

        configureApp?.Invoke(_app);

        _app.UseRouting();

        _app.UseIPRestrictions();

        _app.Map("/", (HttpContext context) => context.Connection.RemoteIpAddress?.ToString())
            .RestrictIP();
    }

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    public HttpClient CreateClient()
    {
        lock (_app)
        {
            if (!_started)
            {
                _app.Start();
                _started = true;
            }
        }

        var client = _app.Services.GetRequiredService<HttpClient>();
        client.BaseAddress = new Uri($"http://localhost:{_port}/");
        return client;
    }

    public HttpRequestMessage CreateRequest(string path, string? xForwardedFor = null)
    {
        var request = new HttpRequestMessage()
        {
            RequestUri = new Uri (new Uri($"http://localhost:{_port}", UriKind.Absolute), path),
        };
        if (xForwardedFor != null)
            request.Headers.Add("X-Forwarded-For", xForwardedFor);
        return request;
    }

    public async Task<IPAddress> GetRemoteIP(string? xForwardedFor = null)
    {
        var client = CreateClient();
        var request = CreateRequest("/", xForwardedFor);
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"GET / should succeed{XForwardedForMessage(xForwardedFor)}");
        var ipString = await response.Content.ReadAsStringAsync();
        return IPAddress.Parse(ipString);
    }

    public async Task<string> InvokeGet(string path, string? xForwardedFor = null, Dictionary<string, string>? headers = null)
    {
        var client = CreateClient();
        var request = CreateRequest(path, xForwardedFor);
        foreach (var (key, value) in headers ?? [])
            request.Headers.Add(key, value);
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"GET {path} should succeed{XForwardedForMessage(xForwardedFor)}");
        return await response.Content.ReadAsStringAsync();
    }

    public async Task EnsureFailure(string path = "/", string? xForwardedFor = null, Dictionary<string, string>? headers = null)
    {
        var client = CreateClient();
        var request = CreateRequest(path, xForwardedFor);
        foreach (var (key, value) in headers ?? [])
            request.Headers.Add(key, value);
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            $"GET {path} should be forbidden{XForwardedForMessage(xForwardedFor)}");
        (await response.Content.ReadAsStringAsync()).Should().Be("Access denied");
    }

    private static string XForwardedForMessage(string? xForwardedFor) =>
        xForwardedFor == null ? "" : $" with X-Forwarded-For: {xForwardedFor}";

    private static int GetPortRoot()
    {
        // A distinct range per target framework, since dotnet test runs them in parallel
        #if NET10_0_OR_GREATER
            return 10100;
        #elif NET9_0_OR_GREATER
            return 10200;
        #elif NET8_0_OR_GREATER
            return 10300;
        #elif NET7_0_OR_GREATER
            return 10400;
        #elif NET6_0_OR_GREATER
            return 10500;
        #else
            throw new NotImplementedException();
        #endif
    }
}
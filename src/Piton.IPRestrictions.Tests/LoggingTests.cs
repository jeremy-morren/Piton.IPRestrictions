using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Piton.IPRestrictions.Endpoints;
using Xunit;

namespace Piton.IPRestrictions.Tests;

public class LoggingTests
{
    private const string MicrosoftCategory = "Microsoft.AspNetCore.IPRestrictions";
    private const string MiddlewareCategory = "Piton.IPRestrictions.Endpoints.IPRestrictionsMiddleware";
    private const string OptionsCategory = "Piton.IPRestrictions.Endpoints.IPRestrictionsOptions";

    [Fact]
    public void MicrosoftLoggingNamespaceShouldBeTheDefault()
    {
        new IPRestrictionsOptions().UseMicrosoftLoggingNamespace.Should().BeTrue();
    }

    [Theory]
    [InlineData(66, true)]
    [InlineData(67, false)]
    public async Task MiddlewareShouldLogUnderTheConfiguredCategory(int testPort, bool useMicrosoftLoggingNamespace)
    {
        var provider = new CapturingLoggerProvider();
        await using var app = CreateApp(testPort, provider, logging => logging.SetMinimumLevel(LogLevel.Trace),
            o => o.UseMicrosoftLoggingNamespace = useMicrosoftLoggingNamespace);

        await app.InvokeGet("/", IPRestrictionsMetricsTests.AllowedIP);
        await app.EnsureFailure("/", IPRestrictionsMetricsTests.DeniedIP);

        var category = useMicrosoftLoggingNamespace ? MicrosoftCategory : MiddlewareCategory;
        var entries = provider.Entries.Where(e => e.Category == category).ToList();
        entries.Should().Contain(e => e.Message.StartsWith("Request from 203.0.113.5 matched allowed"));
        entries.Should().Contain(e => e.Message == "Forbidden request from IP 198.51.100.1");

        // Only one of the two middleware categories is used
        provider.Entries.Should().NotContain(e =>
            e.Category == (useMicrosoftLoggingNamespace ? MiddlewareCategory : MicrosoftCategory));
    }

    [Theory]
    [InlineData(68, true)]
    [InlineData(69, false)]
    public async Task MicrosoftAspNetCoreLogLevelShouldOnlyApplyToTheMicrosoftNamespace(int testPort, bool useMicrosoftLoggingNamespace)
    {
        var provider = new CapturingLoggerProvider();
        // The usual configuration: "Microsoft.AspNetCore": "Warning"
        await using var app = CreateApp(testPort, provider, logging => logging
                .SetMinimumLevel(LogLevel.Trace)
                .AddFilter("Microsoft.AspNetCore", LogLevel.Warning),
            o =>
            {
                o.UseMicrosoftLoggingNamespace = useMicrosoftLoggingNamespace;
                o.AllowedIPs.Add("not-an-ip");
            });

        await app.InvokeGet("/", IPRestrictionsMetricsTests.AllowedIP);
        await app.EnsureFailure("/", IPRestrictionsMetricsTests.DeniedIP);

        // The options always log under Piton.*: the warning about the invalid entry gets through either way
        provider.Entries.Should().Contain(e =>
            e.Category == OptionsCategory && e.Level == LogLevel.Warning && e.Message.Contains("not-an-ip"));

        if (useMicrosoftLoggingNamespace)
            // The middleware's debug messages are filtered out by "Microsoft.AspNetCore": "Warning"
            provider.Entries.Should().NotContain(e => e.Category == MicrosoftCategory || e.Category == MiddlewareCategory);
        else
            // Under Piton.*, the filter doesn't apply
            provider.Entries.Should().Contain(e => e.Category == MiddlewareCategory && e.Level == LogLevel.Debug);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OptionsShouldAlwaysLogUnderPiton(bool useMicrosoftLoggingNamespace)
    {
        var provider = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(provider).SetMinimumLevel(LogLevel.Trace));
        services.AddIPRestrictions(o =>
        {
            o.UseMicrosoftLoggingNamespace = useMicrosoftLoggingNamespace;
            o.AllowedIPs.Add("not-an-ip");
        });
        using var serviceProvider = services.BuildServiceProvider();

        _ = serviceProvider.GetRequiredService<IOptions<IPRestrictionsOptions>>().Value;

        provider.Entries.Should().Contain(e =>
            e.Category == OptionsCategory && e.Level == LogLevel.Warning && e.Message.Contains("not-an-ip"));
        provider.Entries.Should().NotContain(e => e.Category == MicrosoftCategory);
    }

    private static TestWebApp CreateApp(
        int testPort,
        CapturingLoggerProvider provider,
        Action<ILoggingBuilder> configureLogging,
        Action<IPRestrictionsOptions> configureOptions) =>
        new(testPort: testPort,
            configureOptions: o =>
            {
                o.AllowLoopbackIPs = false;
                o.AllowedIPs.Add("203.0.113.0/24");
                configureOptions(o);
            },
            useForwardedHeaders: true,
            configureServices: services => services.AddLogging(logging =>
            {
                logging.AddProvider(provider);
                configureLogging(logging);
            }));

    public record LogEntry(string Category, LogLevel Level, string Message);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, Entries);

        public void Dispose() { }

        private sealed class Logger(string category, ConcurrentQueue<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                entries.Enqueue(new LogEntry(category, logLevel, formatter(state, exception)));
        }
    }
}

using AspNetCore.IPRestrictions.Endpoints;
using Microsoft.Extensions.DependencyInjection;

// The OpenTelemetry convention: instrumentation extensions live in the namespace of the builder they extend
namespace OpenTelemetry.Metrics;

/// <summary>
/// Extension methods to add the IP restrictions metrics to a <see cref="MeterProviderBuilder"/>.
/// </summary>
public static class IPRestrictionsMeterProviderBuilderExtensions
{
    /// <summary>
    /// Enables the metrics of the IP restrictions middleware (the <see cref="IPRestrictionsMetrics.MeterName"/> meter).
    /// </summary>
    /// <param name="builder">The <see cref="MeterProviderBuilder"/> to add the metrics to.</param>
    /// <returns>The builder, so that further calls can be chained.</returns>
    /// <remarks>
    /// See <see cref="IPRestrictionsMetrics"/> for the instruments, and <see cref="IPRestrictionsMetricsOptions"/>
    /// (<see cref="IPRestrictionsOptions.Metrics"/>) for which are recorded.
    /// </remarks>
    public static MeterProviderBuilder AddIPRestrictionsInstrumentation(this MeterProviderBuilder builder) =>
        builder.AddIPRestrictionsInstrumentation(configure: null);

    /// <summary>
    /// Enables the metrics of the IP restrictions middleware (the <see cref="IPRestrictionsMetrics.MeterName"/> meter),
    /// and configures which are recorded.
    /// </summary>
    /// <param name="builder">The <see cref="MeterProviderBuilder"/> to add the metrics to.</param>
    /// <param name="configure">Configures <see cref="IPRestrictionsOptions.Metrics"/>.</param>
    /// <returns>The builder, so that further calls can be chained.</returns>
    /// <remarks>
    /// <para>
    /// This method configures the application's <see cref="IPRestrictionsOptions"/>, so the meter provider must share
    /// the application's services: build it with <c>services.AddOpenTelemetry().WithMetrics(...)</c>
    /// (OpenTelemetry.Extensions.Hosting), not with <c>Sdk.CreateMeterProviderBuilder()</c>.
    /// </para>
    /// <para>
    /// See <see cref="IPRestrictionsMetrics"/> for the instruments.
    /// </para>
    /// </remarks>
    public static MeterProviderBuilder AddIPRestrictionsInstrumentation(
        this MeterProviderBuilder builder,
        Action<IPRestrictionsMetricsOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (configure != null)
            builder.ConfigureServices(services =>
                services.Configure<IPRestrictionsOptions>(options => configure(options.Metrics)));

        return builder.AddMeter(IPRestrictionsMetrics.MeterName);
    }
}

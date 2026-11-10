namespace Piton.IPRestrictions.Endpoints;

internal static class IPRestrictionsLogging
{
    /// <summary>
    /// The category used when <see cref="IPRestrictionsOptions.UseMicrosoftLoggingNamespace"/> is enabled:
    /// under <c>Microsoft.AspNetCore</c> like ASP.NET Core's own middleware,
    /// so that the usual <c>"Microsoft.AspNetCore": "Warning"</c> logging configuration applies to it too.
    /// </summary>
    private const string MicrosoftCategory = "Microsoft.AspNetCore.IPRestrictions";

    /// <summary>
    /// The logger for <typeparamref name="T"/>: <see cref="MicrosoftCategory"/>, or the type's own (<c>Piton.*</c>) category.
    /// </summary>
    public static ILogger CreateLogger<T>(this ILoggerFactory loggerFactory, IPRestrictionsOptions options) =>
        options.UseMicrosoftLoggingNamespace
            ? loggerFactory.CreateLogger(MicrosoftCategory)
            : loggerFactory.CreateLogger<T>();
}

namespace Piton.IPRestrictions.Endpoints;

/// <summary>
/// Extension methods for <see cref="IApplicationBuilder"/> to add the IP Restrictions middleware.
/// </summary>
public static class IPRestrictionsApplicationBuilderExtensions
{
    /// <summary>
    /// Restricts access to the application based on the client IP address.
    /// This middleware must be added after <see cref="Microsoft.AspNetCore.Builder.EndpointRoutingApplicationBuilderExtensions.UseRouting">UseRouting()</see>,
    /// since it needs the endpoint metadata.
    /// </summary>
    /// <remarks>
    /// The IP restrictions are applied wherever the middleware is registered in the pipeline: middleware registered
    /// before it (such as authentication and authorization) runs first, even for requests that it then denies.
    /// </remarks>
    public static IApplicationBuilder UseIPRestrictions(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

#if NET7_0_OR_GREATER
        // From .NET 7 onwards, UseRouting() adds a marker to the application builder properties
        const string routingMiddlewareKey = "__EndpointRouteBuilder";

        if (!app.Properties.ContainsKey(routingMiddlewareKey))
            throw new InvalidOperationException(
                $"The {nameof(IPRestrictionsMiddleware)} must be added after the routing middleware");
#endif

        const string middlewareKey = "__IPRestrictionsMiddlewareSet";

        if (app.Properties.ContainsKey(middlewareKey))
            throw new InvalidOperationException(
                $"The {nameof(IPRestrictionsMiddleware)} has already been added");

        app.Properties[middlewareKey] = true;

        return app.UseMiddleware<IPRestrictionsMiddleware>();
    }
}
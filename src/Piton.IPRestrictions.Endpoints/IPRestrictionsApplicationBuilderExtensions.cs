namespace Piton.IPRestrictions.Endpoints;

/// <summary>
/// Extension methods for <see cref="IApplicationBuilder"/> to add the IP Restrictions middleware.
/// </summary>
public static class IPRestrictionsApplicationBuilderExtensions
{
    /// <summary>
    /// Restricts access to the application based on the client IP address.
    /// This middleware should be added after <see cref="Microsoft.AspNetCore.Builder.EndpointRoutingApplicationBuilderExtensions.UseRouting">UseRouting()</see>
    /// and before <see cref="Microsoft.AspNetCore.Builder.AuthAppBuilderExtensions.UseAuthentication">UseAuthentication()</see>.
    /// </summary>
    /// <remarks>
    /// The middleware must be added after routing because it needs to access the endpoint metadata.
    /// However, it must be added before authentication so that it can block requests before they reach authentication.
    /// </remarks>
    public static IApplicationBuilder UseIPRestrictions(this IApplicationBuilder app)
    {
#if NET7_0_OR_GREATER
        // From .NET 7 onwards, the application builder methods add a marker to the application builder properties
        // to indicate that the routing and authentication middleware have been added.
        
        const string routingMiddlewareKey = "__EndpointRouteBuilder";
        const string authenticationMiddlewareKey = "__AuthenticationMiddlewareSet";
        
        if (!app.Properties.ContainsKey(routingMiddlewareKey))
            throw new InvalidOperationException(
                $"The {nameof(IPRestrictionsMiddleware)} must be added after the routing middleware");

        if (app.Properties.ContainsKey(authenticationMiddlewareKey))
            throw new InvalidOperationException(
                $"The {nameof(IPRestrictionsMiddleware)} must be added before the authentication middleware");
#endif

        const string middlewareKey = "__IPRestrictionsMiddlewareSet";

        if (app.Properties.ContainsKey(middlewareKey))
            throw new InvalidOperationException(
                $"The {nameof(IPRestrictionsMiddleware)} has already been added");

        app.Properties[middlewareKey] = true;

        return app.UseMiddleware<IPRestrictionsMiddleware>();
    }
}
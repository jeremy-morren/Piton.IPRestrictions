
namespace Piton.IPRestrictions.Endpoints;

/// <summary>
/// Indicates that client IP restrictions should be applied to the action or controller
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RestrictIPAttribute : Attribute
{
    /// <inheritdoc/>>
    public override string ToString() =>
        Predicate != null ? "RestrictIP (Predicate)" : "RestrictIP";

    /// <summary>
    /// Indicates that client IP restrictions should be applied to the action or controller
    /// </summary>
    public RestrictIPAttribute() {}

    /// <summary>
    /// A predicate that takes the HttpContext and returns true if IP restrictions should be applied
    /// </summary>
    public Predicate<HttpContext>? Predicate { get; }

    /// <summary>
    /// Creates a new instance of the <see cref="RestrictIPAttribute"/> class with the specified predicate.
    /// </summary>
    /// <param name="predicate">A predicate that takes the HttpContext and returns true if IP restrictions should be applied</param>
    public RestrictIPAttribute(Predicate<HttpContext> predicate)
    {
        Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
    }
}
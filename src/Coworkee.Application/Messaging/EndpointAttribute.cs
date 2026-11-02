namespace Coworkee.Application.Messaging;

/// <summary>
/// Maps a request as an HTTP endpoint that sends it through <see cref="IDispatcher"/>; UseCoworkee maps the requests of all module assemblies.
/// <see cref="Route"/> is absolute like "/api/v1/products/{id:guid}". A POST, PUT or PATCH without route values reads the whole request from the
/// body; every other request binds like [AsParameters]: properties named like a route value from the route, simple ones from the query, complex
/// ones from the body.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public class EndpointAttribute(string method, string route) : Attribute
{
    public string Method { get; } = method;

    public string Route { get; } = route;

    /// <summary>OpenAPI group; defaults to the first route segment after /api/v{n}, like "Products".</summary>
    public string? Tag { get; init; }

    /// <summary>Endpoints require a signed-in user unless this is set; permissions are checked by the pipeline either way.</summary>
    public bool AllowAnonymous { get; init; }
}

public sealed class GetAttribute(string route) : EndpointAttribute("GET", route);

public sealed class PostAttribute(string route) : EndpointAttribute("POST", route);

public sealed class PutAttribute(string route) : EndpointAttribute("PUT", route);

public sealed class PatchAttribute(string route) : EndpointAttribute("PATCH", route);

public sealed class DeleteAttribute(string route) : EndpointAttribute("DELETE", route);

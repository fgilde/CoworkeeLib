using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Coworkee.AspNetCore.Http;

public static partial class RequestEndpointExtensions
{
    private static readonly string[] BodyMethods = ["POST", "PUT", "PATCH"];

    /// <summary>Maps the requests of <paramref name="assemblies"/> that carry an <see cref="EndpointAttribute"/>; UseCoworkee does this for all modules.</summary>
    public static IEndpointRouteBuilder MapCoworkeeRequests(this IEndpointRouteBuilder endpoints, params IEnumerable<Assembly> assemblies)
    {
        foreach (var type in assemblies.Distinct().SelectMany(a => a.GetTypes()).Where(t => t is { IsAbstract: false, IsGenericTypeDefinition: false }))
        {
            foreach (var attribute in type.GetCustomAttributes<EndpointAttribute>())
            {
                endpoints.MapCoworkeeRequest(type, attribute);
            }
        }

        return endpoints;
    }

    private static void MapCoworkeeRequest(this IEndpointRouteBuilder endpoints, Type request, EndpointAttribute attribute)
    {
        var result = request.GetInterfaces().SingleOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>))?.GetGenericArguments()[0]
            ?? throw new InvalidOperationException($"'{request.Name}' carries [{attribute.GetType().Name}] but is no IRequest<T>.");
        // the group carries the version (/api/v2 maps to version 2); the rest of the route is the pattern
        var prefix = VersionPrefix().Match(attribute.Route).Value;
        var group = endpoints.MapCoworkeeApi(prefix).WithTags(attribute.Tag ?? TagOf(attribute.Route));
        var pattern = attribute.Route[prefix.Length..] is { Length: > 0 } rest ? rest : "/";
        var method = attribute.Method.ToUpperInvariant();
        var fromBody = BodyMethods.Contains(method) && RoutePatternFactory.Parse(attribute.Route).Parameters.Count == 0;

        var (map, arguments) = result switch
        {
            _ when result == typeof(Result) => (nameof(MapResult), new[] { request }),
            { IsGenericType: true } when result.GetGenericTypeDefinition() == typeof(Result<>) => (nameof(MapResultOf), [request, result.GetGenericArguments()[0]]),
            _ => (nameof(MapValue), [request, result]),
        };
        var builder = (RouteHandlerBuilder)typeof(RequestEndpointExtensions).GetMethod(map, BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(arguments).Invoke(null, [group, pattern, method, fromBody])!;

        builder.WithName(request.FullName!.Replace('+', '.'));
        if (attribute.AllowAnonymous)
        {
            builder.AllowAnonymous();
        }
        else
        {
            builder.RequireAuthorization();
        }
    }

    private static RouteHandlerBuilder MapResultOf<TRequest, T>(RouteGroupBuilder group, string pattern, string method, bool fromBody) where TRequest : IRequest<Result<T>> =>
        fromBody
            ? group.MapMethods(pattern, [method], ([FromBody] TRequest request, [FromServices] IDispatcher d, CancellationToken ct) => d.SendAsync(request, ct).ToHttpResult())
            : group.MapMethods(pattern, [method], ([AsParameters] TRequest request, [FromServices] IDispatcher d, CancellationToken ct) => d.SendAsync(request, ct).ToHttpResult());

    private static RouteHandlerBuilder MapResult<TRequest>(RouteGroupBuilder group, string pattern, string method, bool fromBody) where TRequest : IRequest<Result> =>
        fromBody
            ? group.MapMethods(pattern, [method], ([FromBody] TRequest request, [FromServices] IDispatcher d, CancellationToken ct) => d.SendAsync(request, ct).ToHttpResult())
            : group.MapMethods(pattern, [method], ([AsParameters] TRequest request, [FromServices] IDispatcher d, CancellationToken ct) => d.SendAsync(request, ct).ToHttpResult());

    private static RouteHandlerBuilder MapValue<TRequest, T>(RouteGroupBuilder group, string pattern, string method, bool fromBody) where TRequest : IRequest<T> =>
        fromBody
            ? group.MapMethods(pattern, [method], async ([FromBody] TRequest request, [FromServices] IDispatcher d, CancellationToken ct) => TypedResults.Ok(await d.SendAsync(request, ct)))
            : group.MapMethods(pattern, [method], async ([AsParameters] TRequest request, [FromServices] IDispatcher d, CancellationToken ct) => TypedResults.Ok(await d.SendAsync(request, ct)));

    /// <summary>"/api/v1/sales-orders/{id}" becomes "Sales orders".</summary>
    private static string TagOf(string route)
    {
        var segment = route.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .SkipWhile(s => s.Equals("api", StringComparison.OrdinalIgnoreCase) || (s.Length > 1 && s[0] is 'v' or 'V' && s[1..].All(char.IsAsciiDigit)))
            .FirstOrDefault(s => !s.StartsWith('{')) ?? "Requests";
        var words = segment.Replace('-', ' ');
        return char.ToUpper(words[0], CultureInfo.InvariantCulture) + words[1..];
    }

    [GeneratedRegex(@"^/?api/v\d{1,4}(?=/|$)", RegexOptions.IgnoreCase)]
    private static partial Regex VersionPrefix();
}

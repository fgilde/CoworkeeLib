using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace Coworkee.SourceGenerators;

/// <summary>Reads a service interface into routes and bindings, by convention and the attributes of Coworkee.Contracts.Services.</summary>
internal sealed class ServiceModelReader(Compilation compilation, List<Diagnostic> diagnostics)
{
    private const string Namespace = "Coworkee.Contracts.Services.";
    internal static readonly Regex Token = new(@"\{\*?([A-Za-z_][A-Za-z0-9_]*)[^}]*\}", RegexOptions.Compiled);

    private readonly INamedTypeSymbol? _marker = compilation.GetTypeByMetadataName(Namespace + "IApplicationService");
    private readonly INamedTypeSymbol? _serviceAttribute = compilation.GetTypeByMetadataName(Namespace + "ApplicationServiceAttribute");
    private readonly INamedTypeSymbol? _operationAttribute = compilation.GetTypeByMetadataName(Namespace + "ServiceOperationAttribute");
    private readonly INamedTypeSymbol? _parameterAttribute = compilation.GetTypeByMetadataName(Namespace + "ServiceParameterAttribute");
    private readonly INamedTypeSymbol? _cancellationToken = compilation.GetTypeByMetadataName("System.Threading.CancellationToken");

    public bool IsAvailable => _marker is not null;

    public bool IsService(INamedTypeSymbol type) =>
        type.TypeKind == TypeKind.Interface && !type.IsGenericType && !SymbolEqualityComparer.Default.Equals(type, _marker)
        && (type.AllInterfaces.Contains(_marker!, SymbolEqualityComparer.Default) || Attribute(type, _serviceAttribute) is not null);

    public ServiceModel? Read(INamedTypeSymbol service, Location location)
    {
        var members = service.GetMembers().Concat(service.AllInterfaces.SelectMany(i => i.GetMembers())).Where(m => m.IsAbstract && !m.IsStatic).ToList();
        var other = members.FirstOrDefault(m => m is not IMethodSymbol { MethodKind: MethodKind.Ordinary });
        if (other is not null)
        {
            Report(Diagnostics.NotCallable, location, other.Name, service.Name, "application services can only have methods");
            return null;
        }

        var name = Naming.ServiceName(service.Name);
        var attribute = Attribute(service, _serviceAttribute);
        var operations = members.Cast<IMethodSymbol>().Select(m => Operation(service, m, location)).ToList();
        foreach (var duplicate in operations.Where(o => o.HttpMethod is not null).GroupBy(o => o.HttpMethod + " " + Token.Replace(o.Route, "{}")).Where(g => g.Count() > 1))
        {
            Report(Diagnostics.DuplicateRoute, location, duplicate.Last().Method.Name, service.Name, duplicate.Key);
        }

        return new ServiceModel(service, Named(attribute, "Route") ?? Naming.Route(name), Named(attribute, "Tag") ?? Naming.Tag(name), operations);
    }

    private OperationModel Operation(INamedTypeSymbol service, IMethodSymbol method, Location location)
    {
        var attribute = Attribute(method, _operationAttribute);
        var returns = Returns(method.ReturnType);
        var reason = method.IsGenericMethod ? "generic methods are not supported"
            : method.Parameters.Any(p => p.RefKind != RefKind.None) ? "ref and out parameters are not supported"
            : returns is null ? "it must return Task, Task<T>, ValueTask or ValueTask<T>"
            : null;
        var ignored = attribute?.NamedArguments.Any(a => a.Key == "Ignore" && a.Value.Value is true) == true;
        if (ignored || reason is not null)
        {
            if (!ignored)
            {
                Report(Diagnostics.NotCallable, location, method.Name, service.Name, reason!);
            }

            return new OperationModel(method, null, string.Empty, ReturnKind.Task, null, []);
        }

        var name = method.Name.EndsWith("Async", StringComparison.Ordinal) && method.Name.Length > 5 ? method.Name.Substring(0, method.Name.Length - 5) : method.Name;
        var (verb, rest) = Naming.Operation(name);
        verb = Named(attribute, "Method")?.ToUpperInvariant() ?? verb;
        var explicitRoute = Named(attribute, "Route");
        var route = explicitRoute?.Trim('/') ?? ConventionRoute(method, rest);
        var tokens = new HashSet<string>(Token.Matches(route).Cast<Match>().Select(m => m.Groups[1].Value), StringComparer.OrdinalIgnoreCase);
        var parameters = Parameters(service, method, verb, tokens, location);
        foreach (var missing in tokens.Where(t => !parameters.Any(p => p.Binding == Binding.Route && string.Equals(p.WireName, t, StringComparison.OrdinalIgnoreCase))))
        {
            Report(Diagnostics.Binding, location, method.Name, service.Name, $"the route has {{{missing}}}, but no parameter of that name");
        }

        foreach (var unrouted in parameters.Where(p => p.Binding == Binding.Route && !tokens.Contains(p.WireName)))
        {
            Report(Diagnostics.Binding, location, method.Name, service.Name, $"'{unrouted.Symbol.Name}' comes from the route, but the route has no {{{unrouted.WireName}}}");
        }

        var result = returns is ReturnKind.TaskOf or ReturnKind.ValueTaskOf ? ((INamedTypeSymbol)method.ReturnType).TypeArguments[0] : null;
        return new OperationModel(method, verb, route, returns!.Value, result, parameters);
    }

    private string ConventionRoute(IMethodSymbol method, string rest)
    {
        var routed = method.Parameters.Where(p => Source(p) is "Route" || (Source(p) is null && p.Name == "id" && IsSimple(p.Type)));
        var segments = routed.Select(p => "{" + (WireName(p) ?? p.Name) + Constraint(p.Type) + "}").ToList();
        if (rest.Length > 0)
        {
            segments.Add(Naming.Kebab(rest));
        }

        return string.Join("/", segments);
    }

    private List<ParameterModel> Parameters(INamedTypeSymbol service, IMethodSymbol method, string verb, HashSet<string> tokens, Location location)
    {
        var allowsBody = verb is "POST" or "PUT" or "PATCH";
        var parameters = new List<ParameterModel>();
        foreach (var parameter in method.Parameters)
        {
            var wire = WireName(parameter) ?? parameter.Name;
            var element = Element(parameter.Type);
            var binding = SymbolEqualityComparer.Default.Equals(parameter.Type, _cancellationToken) ? Binding.Cancellation
                : Source(parameter) switch
                {
                    "Route" => Binding.Route,
                    "Query" => IsSimple(parameter.Type) || element is not null ? Binding.Query : Binding.QueryObject,
                    "Body" => Binding.Body,
                    _ when tokens.Contains(wire) => Binding.Route,
                    _ when IsSimple(parameter.Type) || element is not null => Binding.Query,
                    _ when allowsBody => Binding.Body,
                    _ => Binding.QueryObject,
                };
            if (binding == Binding.Body && parameters.Any(p => p.Binding == Binding.Body))
            {
                Report(Diagnostics.Binding, location, method.Name, service.Name, $"only one parameter can come from the body, '{parameter.Name}' is the second");
            }

            var properties = binding == Binding.QueryObject
                ? parameter.Type.GetMembers().OfType<IPropertySymbol>()
                    .Where(p => p.DeclaredAccessibility == Accessibility.Public && !p.IsStatic && !p.IsIndexer && p.GetMethod is not null && (IsSimple(p.Type) || Element(p.Type) is not null))
                    .Select(p => p.Name).ToList()
                : [];
            parameters.Add(new ParameterModel(parameter, binding, binding == Binding.QueryObject ? parameter.Name : wire, binding == Binding.Query ? element : null, properties));
        }

        return parameters;
    }

    private static ReturnKind? Returns(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named || named.ContainingNamespace?.ToDisplayString() != "System.Threading.Tasks")
        {
            return null;
        }

        return (named.Name, named.TypeArguments.Length) switch
        {
            ("Task", 0) => ReturnKind.Task,
            ("Task", 1) => ReturnKind.TaskOf,
            ("ValueTask", 0) => ReturnKind.ValueTask,
            ("ValueTask", 1) => ReturnKind.ValueTaskOf,
            _ => null,
        };
    }

    private static string Constraint(ITypeSymbol type) => Underlying(type).SpecialType switch
    {
        SpecialType.System_Int32 => ":int",
        SpecialType.System_Int64 => ":long",
        _ => Underlying(type).ToDisplayString() == "System.Guid" ? ":guid" : string.Empty,
    };

    /// <summary>Types minimal APIs bind from a single route or query value.</summary>
    public static bool IsSimple(ITypeSymbol type)
    {
        type = Underlying(type);
        if (type.TypeKind == TypeKind.Enum || type.SpecialType is (>= SpecialType.System_Boolean and <= SpecialType.System_String) or SpecialType.System_DateTime)
        {
            return true;
        }

        return type.ToDisplayString() is "System.Guid" or "System.DateTimeOffset" or "System.DateOnly" or "System.TimeOnly" or "System.TimeSpan" or "System.Uri";
    }

    private static ITypeSymbol? Element(ITypeSymbol type)
    {
        var element = type switch
        {
            IArrayTypeSymbol array => array.ElementType,
            INamedTypeSymbol { TypeArguments.Length: 1 } named when named.SpecialType != SpecialType.System_Nullable_T
                && named.OriginalDefinition.ToDisplayString() is "System.Collections.Generic.IEnumerable<T>" or "System.Collections.Generic.IReadOnlyList<T>"
                    or "System.Collections.Generic.IReadOnlyCollection<T>" or "System.Collections.Generic.IList<T>" or "System.Collections.Generic.ICollection<T>"
                    or "System.Collections.Generic.List<T>" => named.TypeArguments[0],
            _ => null,
        };
        return element is not null && IsSimple(element) ? element : null;
    }

    private static ITypeSymbol Underlying(ITypeSymbol type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : type;

    private string? Source(IParameterSymbol parameter) =>
        Attribute(parameter, _parameterAttribute) is { } attribute && attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is int source
            ? source switch { 0 => "Route", 1 => "Query", _ => "Body" }
            : null;

    private string? WireName(IParameterSymbol parameter) => Named(Attribute(parameter, _parameterAttribute), "Name");

    private static AttributeData? Attribute(ISymbol symbol, INamedTypeSymbol? type) =>
        type is null ? null : symbol.GetAttributes().FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, type));

    private static string? Named(AttributeData? attribute, string name) =>
        attribute?.NamedArguments.FirstOrDefault(a => a.Key == name).Value.Value as string;

    private void Report(DiagnosticDescriptor descriptor, Location location, params object[] arguments) =>
        diagnostics.Add(Diagnostic.Create(descriptor, location, arguments));
}

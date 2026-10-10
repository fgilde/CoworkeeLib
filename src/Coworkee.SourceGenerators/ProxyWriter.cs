using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using static Coworkee.SourceGenerators.SourceText;

namespace Coworkee.SourceGenerators;

/// <summary>HttpClient proxies for the application services the client sees, registered at assembly load.</summary>
internal static class ProxyWriter
{
    private const string Url = "global::Coworkee.Client.Blazor.ServiceProxies.ServiceProxyUrl";
    public static string Write(IReadOnlyList<ServiceModel> services)
    {
        var source = new StringBuilder(Header);
        var names = services.Select((s, i) => $"{ProxyName(s.Interface.Name)}{(services.Take(i).Any(o => o.Interface.Name == s.Interface.Name) ? i.ToString() : string.Empty)}").ToList();
        source.Append("namespace Coworkee.Generated\n{\n");
        for (var i = 0; i < services.Count; i++)
        {
            Proxy(source, services[i], names[i]);
        }

        source.Append("    internal static class CoworkeeServiceProxies\n    {\n");
        source.Append("        [global::System.Runtime.CompilerServices.ModuleInitializer]\n        internal static void Register()\n        {\n");
        for (var i = 0; i < services.Count; i++)
        {
            source.Append($"            global::Coworkee.Client.Blazor.ServiceProxies.ServiceProxyRegistry.Add<{Type(services[i].Interface)}, {names[i]}>();\n");
        }

        source.Append("        }\n    }\n}\n");
        return source.ToString();
    }

    private static string ProxyName(string interfaceName) =>
        (interfaceName.Length > 1 && interfaceName[0] == 'I' && char.IsUpper(interfaceName[1]) ? interfaceName.Substring(1) : interfaceName) + "Proxy";

    private static void Proxy(StringBuilder source, ServiceModel service, string name)
    {
        source.Append($"    internal sealed class {name}(global::System.Net.Http.HttpClient http) : global::Coworkee.Client.Blazor.Api.ApiClientBase(http), {Type(service.Interface)}\n    {{\n");
        source.Append(string.Join("\n", service.Operations.Select(operation => Method(service, operation))));
        source.Append("    }\n\n");
    }

    private static string Method(ServiceModel service, OperationModel operation)
    {
        var method = operation.Method;
        var parameters = string.Join(", ", method.Parameters.Select(p => $"{Modifier(p.RefKind)}{Type(p.Type)} {Identifier(p.Name)}"));
        var generics = method.IsGenericMethod ? $"<{string.Join(", ", method.TypeParameters.Select(t => t.Name))}>" : string.Empty;
        return $"        {Type(method.ReturnType)} {Type(method.ContainingType)}.{method.Name}{generics}({parameters})\n            => {Body(service, operation)};\n";
    }

    private static string Body(ServiceModel service, OperationModel operation)
    {
        if (operation.HttpMethod is null)
        {
            return $"throw new global::System.NotSupportedException({Literal($"{operation.Method.Name} is not part of the API.")})";
        }

        var body = operation.Parameters.FirstOrDefault(p => p.Binding == Binding.Body) is { } b ? Identifier(b.Symbol.Name) : "null";
        var cancellation = operation.Parameters.FirstOrDefault(p => p.Binding == Binding.Cancellation) is { } c ? Identifier(c.Symbol.Name) : "default";
        var result = operation.Result is null ? string.Empty : $"<{Type(operation.Result)}>";
        var send = $"SendAsync{result}(new global::System.Net.Http.HttpMethod({Literal(operation.HttpMethod)}), {Address(service, operation)}, {body}, {cancellation})";
        return operation.Return switch
        {
            ReturnKind.ValueTask or ReturnKind.ValueTaskOf => $"new {Type(operation.Method.ReturnType)}({send})",
            _ => send,
        };
    }

    private static string Address(ServiceModel service, OperationModel operation)
    {
        var template = service.Route.Trim('/') + (operation.Route.Length > 0 ? "/" + operation.Route : string.Empty);
        var parts = new List<string>();
        var position = 0;
        foreach (Match token in ServiceModelReader.Token.Matches(template))
        {
            parts.Add(Literal(template.Substring(position, token.Index - position)));
            if (operation.Parameters.FirstOrDefault(p => p.Binding == Binding.Route && string.Equals(p.WireName, token.Groups[1].Value, System.StringComparison.OrdinalIgnoreCase)) is { } parameter)
            {
                parts.Add($"{Url}.Segment({Identifier(parameter.Symbol.Name)})");
            }

            position = token.Index + token.Length;
        }

        parts.Add(Literal(template.Substring(position)));
        var query = operation.Parameters.SelectMany(Query).ToList();
        if (query.Count > 0)
        {
            parts.Add($"{Url}.Query({string.Join(", ", query)})");
        }

        return string.Join(" + ", parts.Where(p => p != "\"\""));
    }

    private static IEnumerable<string> Query(ParameterModel parameter) => parameter.Binding switch
    {
        Binding.Query => [$"({Literal(parameter.WireName)}, (object?){Identifier(parameter.Symbol.Name)})"],
        Binding.QueryObject => parameter.QueryProperties.Select(p => $"({Literal(Naming.Camel(p))}, (object?){Identifier(parameter.Symbol.Name)}{Access(parameter.Symbol.Type)}{p})"),
        _ => [],
    };

    private static string Access(ITypeSymbol type) => type.IsValueType && type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T ? "." : "?.";

    private static string Modifier(RefKind kind) => kind switch
    {
        RefKind.Ref => "ref ",
        RefKind.Out => "out ",
        RefKind.In => "in ",
        _ => string.Empty,
    };
}

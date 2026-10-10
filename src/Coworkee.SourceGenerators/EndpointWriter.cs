using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using static Coworkee.SourceGenerators.SourceText;

namespace Coworkee.SourceGenerators;

/// <summary>Minimal API endpoints for the application services an assembly implements, registered at assembly load.</summary>
internal static class EndpointWriter
{
    private const string Permission = "Coworkee.Application.Authorization.RequiresPermissionAttribute";

    public static string Write(IReadOnlyList<(ServiceModel Service, INamedTypeSymbol Implementation)> services)
    {
        var source = new StringBuilder(Header);
        source.Append("namespace Coworkee.Generated\n{\n    using global::Microsoft.AspNetCore.Builder;\n    using global::Microsoft.AspNetCore.Http;\n\n    internal static class CoworkeeApplicationServiceEndpoints\n    {\n");
        source.Append("        [global::System.Runtime.CompilerServices.ModuleInitializer]\n        internal static void Register()\n        {\n");
        for (var i = 0; i < services.Count; i++)
        {
            var (service, implementation) = services[i];
            source.Append($"            global::Coworkee.AspNetCore.ApplicationServices.ApplicationServiceRegistry.Add<{Type(service.Interface)}, {Type(implementation)}>({Literal(service.Route)}, {Literal(service.Tag)}, Map{i});\n");
        }

        source.Append("        }\n");
        for (var i = 0; i < services.Count; i++)
        {
            var (service, implementation) = services[i];
            source.Append($"\n        private static void Map{i}(global::Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints)\n        {{\n");
            foreach (var operation in service.Operations.Where(o => o.HttpMethod is not null))
            {
                Endpoint(source, service, implementation, operation);
            }

            source.Append("        }\n");
        }

        source.Append("    }\n}\n");
        return source.ToString();
    }

    private static void Endpoint(StringBuilder source, ServiceModel service, INamedTypeSymbol implementation, OperationModel operation)
    {
        var parameters = operation.Parameters.Select(Parameter).Append($"[global::Microsoft.AspNetCore.Mvc.FromServicesAttribute] {Type(service.Interface)} __service");
        var arguments = string.Join(", ", operation.Parameters.Select(p => p.Element is null ? Identifier(p.Symbol.Name) : $"[.. {Identifier(p.Symbol.Name)}]"));
        var call = $"await __service.{operation.Method.Name}({arguments})";
        var body = operation.Result is null
            ? $"{{ {call}; return global::Microsoft.AspNetCore.Http.TypedResults.NoContent(); }}"
            : $"global::Microsoft.AspNetCore.Http.TypedResults.Ok({call})";
        source.Append($"            global::Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapMethods(endpoints, {Literal(operation.Route)}, new[] {{ {Literal(operation.HttpMethod!)} }},\n");
        source.Append($"                static async ({string.Join(", ", parameters)}) => {body})");
        foreach (var permission in Permissions(implementation, operation.Method))
        {
            source.Append($"\n                .WithMetadata(new global::{Permission}({Literal(permission)}))");
        }

        source.Append(";\n");
    }

    private static string Parameter(ParameterModel parameter)
    {
        var name = Identifier(parameter.Symbol.Name);
        var type = parameter.Element is null ? Type(parameter.Symbol.Type) : Type(parameter.Element) + "[]";
        return parameter.Binding switch
        {
            Binding.Route => $"[global::Microsoft.AspNetCore.Mvc.FromRouteAttribute(Name = {Literal(parameter.WireName)})] {type} {name}",
            Binding.Query => $"[global::Microsoft.AspNetCore.Mvc.FromQueryAttribute(Name = {Literal(parameter.WireName)})] {type} {name}",
            Binding.QueryObject => $"[global::Microsoft.AspNetCore.Http.AsParametersAttribute] {type} {name}",
            Binding.Body => $"[global::Microsoft.AspNetCore.Mvc.FromBodyAttribute] {type} {name}",
            _ => $"{type} {name}",
        };
    }

    private static IEnumerable<string> Permissions(INamedTypeSymbol implementation, IMethodSymbol method)
    {
        var implemented = implementation.FindImplementationForInterfaceMember(method);
        return implementation.GetAttributes().Concat(implemented?.GetAttributes() ?? [])
            .Where(a => a.AttributeClass?.ToDisplayString() == Permission && a.ConstructorArguments.Length == 1)
            .Select(a => a.ConstructorArguments[0].Value as string)
            .OfType<string>()
            .Distinct();
    }
}

using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Coworkee.SourceGenerators;

/// <summary>
/// Server (Coworkee.AspNetCore referenced): endpoints for the application services the assembly implements.
/// Client (Coworkee.Client.Blazor referenced): proxies for the application services it sees; MSBuild property CoworkeeServiceProxies=false turns them off.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ApplicationServiceGenerator : IIncrementalGenerator
{
    private const string ServerMarker = "Coworkee.AspNetCore.ApplicationServices.ApplicationServiceRegistry";
    private const string ClientMarker = "Coworkee.Client.Blazor.ServiceProxies.ServiceProxyRegistry";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var proxiesEnabled = context.AnalyzerConfigOptionsProvider.Select((options, _) =>
            !options.GlobalOptions.TryGetValue("build_property.CoworkeeServiceProxies", out var value) || !string.Equals(value, "false", System.StringComparison.OrdinalIgnoreCase));
        context.RegisterSourceOutput(context.CompilationProvider.Combine(proxiesEnabled), (output, input) => Generate(output, input.Left, input.Right));
    }

    private static void Generate(SourceProductionContext output, Compilation compilation, bool proxiesEnabled)
    {
        var diagnostics = new List<Diagnostic>();
        var reader = new ServiceModelReader(compilation, diagnostics);
        if (!reader.IsAvailable)
        {
            return;
        }

        if (compilation.GetTypeByMetadataName(ServerMarker) is not null)
        {
            var implemented = Types(compilation.Assembly.GlobalNamespace)
                .Where(t => t.TypeKind == TypeKind.Class && !t.IsAbstract && !t.IsGenericType && t.DeclaredAccessibility != Accessibility.Private)
                .SelectMany(t => t.AllInterfaces.Where(reader.IsService).Select(i => (Interface: i, Implementation: t)))
                .GroupBy(p => p.Interface, SymbolEqualityComparer.Default)
                .Select(g => g.First())
                .Select(p => (Service: reader.Read(p.Interface, Location(p.Interface, p.Implementation)), p.Implementation))
                .Where(p => p.Service is not null)
                .Select(p => (p.Service!, p.Implementation))
                .ToList();
            if (implemented.Count > 0)
            {
                output.AddSource("CoworkeeApplicationServiceEndpoints.g.cs", EndpointWriter.Write(implemented));
            }
        }

        if (proxiesEnabled && compilation.GetTypeByMetadataName(ClientMarker) is not null)
        {
            var contracts = compilation.GetTypeByMetadataName("Coworkee.Contracts.Services.IApplicationService")!.ContainingAssembly;
            var assemblies = compilation.SourceModule.ReferencedAssemblySymbols
                .Where(a => SymbolEqualityComparer.Default.Equals(a, contracts) || a.Modules.Any(m => m.ReferencedAssemblySymbols.Contains(contracts, SymbolEqualityComparer.Default)))
                .Append(compilation.Assembly);
            var services = assemblies.SelectMany(a => Types(a.GlobalNamespace))
                .Where(t => reader.IsService(t) && compilation.IsSymbolAccessibleWithin(t, compilation.Assembly))
                .Select(t => reader.Read(t, Location(t, null)))
                .OfType<ServiceModel>()
                .ToList();
            if (services.Count > 0)
            {
                output.AddSource("CoworkeeServiceProxies.g.cs", ProxyWriter.Write(services));
            }
        }

        foreach (var diagnostic in diagnostics.Distinct())
        {
            output.ReportDiagnostic(diagnostic);
        }
    }

    private static Location Location(INamedTypeSymbol service, INamedTypeSymbol? implementation) =>
        service.Locations.FirstOrDefault(l => l.IsInSource) ?? implementation?.Locations.FirstOrDefault(l => l.IsInSource) ?? Microsoft.CodeAnalysis.Location.None;

    private static IEnumerable<INamedTypeSymbol> Types(INamespaceSymbol root)
    {
        foreach (var member in root.GetMembers())
        {
            var nested = member is INamespaceSymbol ns ? Types(ns) : Nested((INamedTypeSymbol)member);
            foreach (var type in nested)
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> Nested(INamedTypeSymbol type) => new[] { type }.Concat(type.GetTypeMembers().SelectMany(Nested));
}

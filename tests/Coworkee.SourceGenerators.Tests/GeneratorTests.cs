using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Coworkee.SourceGenerators.Tests;

/// <summary>Runs the generator on small sources; COWORKEE_UPDATE_SNAPSHOTS=1 rewrites the snapshots.</summary>
public sealed class GeneratorTests
{
    private const string Contract = """
        using Coworkee.Contracts.Services;

        namespace Shop;

        public sealed record OrderDto(System.Guid Id, string Customer);

        public sealed record PlaceOrder(string Customer);

        public sealed record OrderPage(int Skip, int Take, string? Customer);

        [ApplicationService(Tag = "Shop orders")]
        public interface IOrderAppService : IApplicationService
        {
            // members

            System.Threading.Tasks.Task<OrderDto> GetAsync(System.Guid id, System.Threading.CancellationToken cancellationToken = default);

            System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<OrderDto>> GetListAsync(OrderPage page);

            System.Threading.Tasks.Task<OrderDto> PlaceAsync(PlaceOrder order);

            [ServiceOperation(Method = "POST", Route = "delete")]
            System.Threading.Tasks.Task DeleteManyAsync([ServiceParameter(ParameterSource.Body)] System.Guid[] ids);
        }
        """;

    private const string Implementation = """
        namespace Shop;

        internal sealed class OrderAppService : IOrderAppService
        {
            public System.Threading.Tasks.Task<OrderDto> GetAsync(System.Guid id, System.Threading.CancellationToken cancellationToken) => null!;

            public System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<OrderDto>> GetListAsync(OrderPage page) => null!;

            [Coworkee.Application.Authorization.RequiresPermission("Orders.Place")]
            public System.Threading.Tasks.Task<OrderDto> PlaceAsync(PlaceOrder order) => null!;

            public System.Threading.Tasks.Task DeleteManyAsync(System.Guid[] ids) => null!;
        }
        """;

    [Theory]
    [InlineData("CoworkeeApplicationServiceEndpoints.g.cs")]
    [InlineData("CoworkeeServiceProxies.g.cs")]
    public void Output_matches_the_snapshot_and_compiles(string file)
    {
        var (compilation, generated, _) = Run(Contract, Implementation);

        compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        Snapshot(file, generated.Single(t => t.FilePath.EndsWith(file, StringComparison.Ordinal)).ToString());
    }

    [Theory]
    [InlineData("int Count();", "CWK001")]
    [InlineData("string Name { get; }", "CWK001")]
    [InlineData("System.Threading.Tasks.Task SaveAsync(PlaceOrder a, PlaceOrder b);", "CWK002")]
    [InlineData("[ServiceOperation(Route = \"{id}\")] System.Threading.Tasks.Task ArchiveAsync(string name);", "CWK002")]
    [InlineData("System.Threading.Tasks.Task<OrderDto> GetAllAsync();", "CWK003")]
    public void Reports_contracts_that_cannot_be_exposed(string member, string id)
    {
        var (_, _, diagnostics) = Run(Contract.Replace("// members", member, StringComparison.Ordinal).Replace("GetListAsync(OrderPage page)", "GetListAsync()", StringComparison.Ordinal), string.Empty);

        diagnostics.Select(d => d.Id).ShouldContain(id);
    }

    [Fact]
    public void Ignored_members_need_no_http_shape()
    {
        var (compilation, _, diagnostics) = Run(Contract.Replace("// members", "[ServiceOperation(Ignore = true)] int Count();", StringComparison.Ordinal), string.Empty);

        diagnostics.ShouldBeEmpty();
        compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("IDocumentTypeAppService", "/api/v1/document-types", "Document types")]
    [InlineData("ICategoryService", "/api/v1/categories", "Categories")]
    [InlineData("IHTTPProxyApplicationService", "/api/v1/http-proxies", "HTTP proxies")]
    public void Services_are_named_by_convention(string name, string route, string tag)
    {
        Naming.Route(Naming.ServiceName(name)).ShouldBe(route);
        Naming.Tag(Naming.ServiceName(name)).ShouldBe(tag);
    }

    [Theory]
    [InlineData("Get", "GET", "")]
    [InlineData("GetList", "GET", "")]
    [InlineData("GetByName", "GET", "ByName")]
    [InlineData("Search", "GET", "Search")]
    [InlineData("Create", "POST", "")]
    [InlineData("Update", "PUT", "")]
    [InlineData("Delete", "DELETE", "")]
    [InlineData("Archive", "POST", "Archive")]
    [InlineData("Getaway", "POST", "Getaway")]
    public void Methods_are_mapped_by_convention(string name, string method, string remainder) =>
        Naming.Operation(name).ShouldBe((method, remainder));

    [Fact]
    public void Proxies_can_be_turned_off()
    {
        var (_, generated, _) = Run(Contract, Implementation, proxies: "false");

        generated.Select(t => Path.GetFileName(t.FilePath)).ShouldBe(["CoworkeeApplicationServiceEndpoints.g.cs"]);
    }

    private static (Compilation Compilation, IReadOnlyList<SyntaxTree> Generated, IReadOnlyList<Diagnostic> Diagnostics) Run(string contract, string implementation, string? proxies = null)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(p => Path.GetFileNameWithoutExtension(p) != typeof(GeneratorTests).Assembly.GetName().Name)
            .Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create("Shop", [CSharpSyntaxTree.ParseText(contract), CSharpSyntaxTree.ParseText(implementation)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var options = new BuildProperties(proxies is null ? [] : new Dictionary<string, string> { ["build_property.CoworkeeServiceProxies"] = proxies });
        CSharpGeneratorDriver.Create([new ApplicationServiceGenerator().AsSourceGenerator()], optionsProvider: options).RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics, TestContext.Current.CancellationToken);
        return (output, output.SyntaxTrees.Skip(2).ToList(), diagnostics);
    }

    private static void Snapshot(string file, string actual, [CallerFilePath] string source = "")
    {
        var path = Path.Combine(Path.GetDirectoryName(source)!, "Snapshots", file + ".txt");
        if (Environment.GetEnvironmentVariable("COWORKEE_UPDATE_SNAPSHOTS") == "1" || !File.Exists(path))
        {
            File.WriteAllText(path, actual);
        }

        actual.ReplaceLineEndings().ShouldBe(File.ReadAllText(path).ReplaceLineEndings());
    }

    private sealed class BuildProperties(Dictionary<string, string> values) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Values(values);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

        private sealed class Values(Dictionary<string, string> values) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) => values.TryGetValue(key, out value);
        }
    }
}

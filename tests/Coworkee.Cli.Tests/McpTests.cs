using System.IO.Pipelines;
using System.Text.Json;
using System.Text.RegularExpressions;
using Coworkee.Cli.Mcp;
using Coworkee.Cli.Spec;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Coworkee.Cli.Tests;

public sealed class McpTests : IAsyncLifetime
{
    private readonly List<(string[] Args, string Directory)> _runs = [];
    private IHost _server = null!;
    private McpClient _client = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var toServer = new Pipe();
        var toClient = new Pipe();
        _server = McpServerHost.Build((args, directory, _) =>
        {
            _runs.Add(([.. args], directory));
            return Task.FromResult(new CliRun(0, "done"));
        }, toServer.Reader.AsStream(), toClient.Writer.AsStream());
        await _server.StartAsync(Ct);
        _client = await McpClient.CreateAsync(new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()), cancellationToken: Ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        await _server.StopAsync();
        _server.Dispose();
    }

    [Fact]
    public async Task Lists_every_tool_with_a_description_and_the_instructions()
    {
        var tools = await _client.ListToolsAsync(cancellationToken: Ct);

        tools.Select(t => t.Name).ShouldBe(
            ["list_options", "get_spec_schema", "get_spec_example", "validate_spec", "create_app", "add_module", "add_entity", "import_entities", "add_migration", "run_doctor", "update_packages", "search_docs"],
            ignoreOrder: true);
        tools.ShouldAllBe(t => !string.IsNullOrWhiteSpace(t.Description));
        tools.Single(t => t.Name == "create_app").JsonSchema.GetRawText().ShouldNotContain("templateSource");
        _client.ServerInfo.Name.ShouldBe("coworkee");
        _client.ServerInstructions!.ShouldContain("create_app");
    }

    [Fact]
    public async Task Read_tools_answer_with_the_catalog_schema_example_and_validation()
    {
        JsonDocument.Parse(await CallAsync("list_options", new() { ["group"] = "Modules" })).RootElement.EnumerateArray()
            .ShouldAllBe(o => o.GetProperty("group").GetString() == "Modules");
        (await CallAsync("get_spec_schema")).ShouldContain("\"eventBus\"");
        SpecValidator.Validate(SpecJson.Read(await CallAsync("get_spec_example"))).ShouldBeEmpty();
        (await CallAsync("validate_spec", new() { ["spec"] = SpecJson.Write(SpecJson.Example()) })).ShouldBe("ok");
        (await CallAsync("validate_spec", new() { ["spec"] = """{ "name": "1" }""" })).ShouldStartWith("name:");
        (await CallAsync("validate_spec", new() { ["spec"] = "{" })).ShouldStartWith("The spec is not valid JSON");
        _runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task Create_app_runs_coworkee_new_with_a_spec_whose_files_are_absolute()
    {
        using var folder = new TempFolder();
        File.WriteAllText(folder.File("logo.svg"), "<svg/>");

        var result = await CallAsync("create_app", new()
        {
            ["directory"] = folder.Path,
            ["name"] = "Crm",
            ["options"] = new Dictionary<string, string> { ["chat"] = "false", ["logo"] = "logo.svg" },
        });

        result.ShouldBe("exit code 0\ndone");
        var (args, directory) = _runs.Single();
        directory.ShouldBe(folder.Path);
        args[..2].ShouldBe(["new", "--spec"]);
        args[3..].ShouldBe(["-o", Path.Combine(folder.Path, "Crm")]);
        Path.GetDirectoryName(args[2]).ShouldBe(folder.Path);
        File.Exists(args[2]).ShouldBeFalse();
        (await CallAsync("create_app", new() { ["directory"] = folder.Path, ["name"] = "Crm", ["options"] = new Dictionary<string, string> { ["logo"] = "../logo.svg" } })).ShouldContain("inside the folder");
        (await CallAsync("create_app", new() { ["directory"] = folder.Path, ["spec"] = """{ "name": "Crm", "output": "../elsewhere" }""" })).ShouldStartWith("output:");
        (await CallAsync("create_app", new() { ["directory"] = folder.Path, ["name"] = "1x" })).ShouldStartWith("name:");
        _runs.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Change_tools_run_the_matching_commands_in_the_app_folder()
    {
        await CallAsync("add_module", new() { ["directory"] = "app", ["module"] = "Sales", ["entity"] = "Order", ["properties"] = new[] { "Number:string:required:50", "Total:decimal" }, ["clientEntity"] = true, ["mapping"] = "Mapperly" });
        await CallAsync("add_entity", new() { ["directory"] = "app", ["module"] = "Sales", ["entity"] = "Invoice", ["plural"] = "Invoices" });
        await CallAsync("import_entities", new() { ["directory"] = "app", ["from"] = "db", ["source"] = "shop.db", ["module"] = "Legacy", ["tables"] = new[] { "orders", "customers" }, ["dryRun"] = true });
        await CallAsync("add_migration", new() { ["directory"] = "app", ["name"] = "AddSales" });
        await CallAsync("update_packages", new() { ["directory"] = "app", ["prerelease"] = true });
        await CallAsync("run_doctor", new() { ["directory"] = "app" });

        _runs.Select(r => r.Directory).ShouldAllBe(d => d == "app");
        _runs.Select(r => string.Join(' ', r.Args)).ShouldBe(
        [
            "module add Sales --entity Order --property Number:string:required:50 --property Total:decimal --client-entity --mapping Mapperly",
            "entity add Sales Invoice --plural Invoices",
            "entity import shop.db --from db --module Legacy --tables orders,customers --dry-run",
            "migrations add AddSales",
            "update --prerelease",
            "doctor",
        ]);
        _runs.Select(r => r.Args).ShouldAllBe(args => CliApp.Build().Parse(args).Errors.Count == 0);
    }

    [Fact]
    public void Offline_docs_list_the_pages_of_the_navigation()
    {
        var nav = File.ReadAllText(Path.Combine(Dotnet.RepositoryRoot(), "website", "mkdocs.yml")).ReplaceLineEndings("\n").Split("\nnav:\n")[1];
        var pages = Regex.Matches(nav, @"-\s+[^:\n]+:\s+(\S+)\.md").Select(m => m.Groups[1].Value == "index" ? string.Empty : m.Groups[1].Value + "/");

        DocsSearch.Pages.Select(p => p.Path).ShouldBe(pages);
        DocsSearch.Offline(["entities"]).ShouldContain("ui/client-entities/");
    }

    [Fact]
    public async Task Docs_search_ranks_the_page_about_the_query_first_and_falls_back_to_titles_offline()
    {
        const string Index = """
            { "docs": [
              { "location": "reference/packages/", "title": "Packages", "text": "<code>client</code> client client entities entities entities client" },
              { "location": "ui/client-entities/", "title": "Client entities", "text": "A client entity is an OData set the browser keeps." },
              { "location": "de/ui/client-entities/", "title": "Client-Entities", "text": "client entities" }
            ] }
            """;
        using var online = new HttpClient(new Answer(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(Index) }));
        using var offline = new HttpClient(new Answer(_ => throw new HttpRequestException("offline")));

        var result = await DocsSearch.SearchAsync("client entities", online, Ct);

        result.Split('\n')[0].ShouldBe("Client entities - https://fgilde.github.io/CoworkeeLib/ui/client-entities/");
        result.ShouldNotContain("de/ui");
        result.ShouldNotContain("<code>");
        (await DocsSearch.SearchAsync("client entities", offline, Ct)).ShouldContain("pages by title");
        (await DocsSearch.SearchAsync("   ", online, Ct)).ShouldBe("Give words to search for.");
        DocsSearch.Score("Title", "text", string.Empty, [string.Empty]).ShouldBe(0);
    }

    [Fact]
    public async Task Child_processes_get_no_input_and_die_with_their_children_when_cancelled()
    {
        var reader = OperatingSystem.IsWindows() ? new[] { "cmd", "/c", "sort" } : ["cat"];
        (await McpServerHost.RunAsync(reader, Path.GetTempPath(), Ct).WaitAsync(TimeSpan.FromSeconds(20), Ct)).ExitCode.ShouldBe(0);

        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var sleeper = OperatingSystem.IsWindows() ? new[] { "cmd", "/c", "ping -n 30 127.0.0.1 > nul" } : ["sh", "-c", "sleep 30"];
        var started = DateTime.UtcNow;

        await Should.ThrowAsync<OperationCanceledException>(() => McpServerHost.RunAsync(sleeper, Path.GetTempPath(), cancel.Token));

        (DateTime.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(15));
    }

    private sealed class Answer(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(answer(request));
    }

    private async Task<string> CallAsync(string tool, Dictionary<string, object?>? arguments = null)
    {
        var result = await _client.CallToolAsync(tool, arguments, cancellationToken: Ct);
        return string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));
    }
}

using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Coworkee.Cli.Mcp;

internal static class McpServerHost
{
    public const string Instructions =
        "Creates and extends Coworkee applications (.NET 10, Aspire, Blazor WebAssembly, OpenIddict, Postgres). "
        + "To create an app: list_options and get_spec_schema (or get_spec_example), write a spec with the user's choices and entities, validate_spec, then create_app. "
        + "To extend an app: add_module, add_entity or import_entities (from a database, OpenAPI or CSV), then add_migration; add_option turns library modules and other options on later (dryRun first). search_docs answers questions about the library.";

    /// <summary>The MCP server over stdio, or over the given streams (tests).</summary>
    public static IHost Build(CliRunner runner, Stream? input = null, Stream? output = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddSingleton(runner);
        builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(10) });
        var mcp = builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new() { Name = "coworkee", Version = CliApp.Version };
            options.ServerInstructions = Instructions;
        }).WithTools<CoworkeeMcpTools>();
        _ = input is null || output is null ? mcp.WithStdioServerTransport() : mcp.WithStreamServerTransport(input, output);
        return builder.Build();
    }

    /// <summary>Runs this CLI again as a child process and collects what it prints.</summary>
    public static Task<CliRun> RunSelfAsync(IReadOnlyList<string> args, string workingDirectory, CancellationToken cancellationToken)
    {
        var self = Path.GetFileNameWithoutExtension(Environment.ProcessPath)!.Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? [Environment.ProcessPath!, typeof(McpServerHost).Assembly.Location]
            : new[] { Environment.ProcessPath! };
        return RunAsync([.. self, .. args], workingDirectory, cancellationToken);
    }

    /// <summary>
    /// Runs a process without input (its stdin is closed, so nothing waits for the MCP client's stream) and collects what it prints;
    /// cancelling kills it with all its children (dotnet new, restore, ef).
    /// </summary>
    public static async Task<CliRun> RunAsync(IReadOnlyList<string> command, string workingDirectory, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(command[0])
        {
            WorkingDirectory = workingDirectory, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var arg in command.Skip(1))
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return new CliRun(process.ExitCode, (await output + await error).Trim());
    }
}

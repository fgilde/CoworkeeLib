using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Coworkee.Cli.Mcp;

internal static class McpServerHost
{
    public const string Instructions =
        "Creates and extends Coworkee applications (.NET 10, Aspire, Blazor WebAssembly, OpenIddict, Postgres). "
        + "To create an app: list_options and get_spec_schema (or get_spec_example), write a spec with the user's choices and entities, validate_spec, then create_app. "
        + "To extend an app: add_module, add_entity, then add_migration. search_docs answers questions about the library.";

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
    public static async Task<CliRun> RunSelfAsync(IReadOnlyList<string> args, string workingDirectory, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath)!.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            start.ArgumentList.Add(typeof(McpServerHost).Assembly.Location);
        }

        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new CliRun(process.ExitCode, (await output + await error).Trim());
    }
}

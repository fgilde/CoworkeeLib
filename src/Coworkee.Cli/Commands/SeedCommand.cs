using System.CommandLine;
using Coworkee.Cli.Generation;

namespace Coworkee.Cli.Commands;

/// <summary>coworkee seed add: writes a seed contributor; the migrations project runs it (coworkee run, coworkee db seed, a deployment).</summary>
internal static class SeedCommand
{
    private static readonly Argument<string> Name = Names.Simple("name", "Name of the seed, for example StandardTags.");
    private static readonly Option<string?> Module = new("--module")
    {
        Description = "Put it into this module (src/<App>.<Module>/Seeds): the api then also runs it for a tenant set up in the browser or created later. Default: the migrations project.",
    };
    private static readonly Option<bool> Host = new("--host") { Description = "Run once per run instead of once per tenant." };
    private static readonly Option<bool> Development = new("--development") { Description = "Run in local runs only (with the demo users)." };
    private static readonly Option<bool> Deployment = new("--deployment") { Description = "Run in deployments only." };

    public static Command Create()
    {
        var add = new Command("add", "Add a seed contributor: data every database needs, checked and written on every migrations run.") { Name, Module, Host, Development, Deployment }
            .Handle(async (result, cancellationToken) =>
            {
                var file = File(Solution.Find(Environment.CurrentDirectory), result.GetValue(Name)!, result.GetValue(Module), result.GetValue(Host), result.GetValue(Development),
                    result.GetValue(Deployment));
                if (System.IO.File.Exists(file.Path))
                {
                    throw new CliException($"{file.Path} exists already.");
                }

                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file.Path)!);
                await System.IO.File.WriteAllTextAsync(file.Path, file.Content.ReplaceLineEndings(), cancellationToken);
                Console.WriteLine($"Seed added: {file.Path}. It runs with the next migrations run (coworkee run, coworkee db seed).");
                return 0;
            });
        return new Command("seed", "Manage seed contributors (coworkee db seed runs them).") { add };
    }

    public static GeneratedFile File(Solution solution, string name, string? module, bool host, bool development, bool deployment)
    {
        var project = module is null
            ? System.IO.Path.GetDirectoryName(solution.Project("Migrations"))!
            : System.IO.Path.Combine(solution.Src, $"{solution.Name}.{module}");
        if (!Directory.Exists(project))
        {
            throw new CliException($"No module {module}: {project} does not exist.");
        }

        var type = name.EndsWith("Seed", StringComparison.Ordinal) ? name : name + "Seed";
        // both environment flags or none: every environment, the default
        var members = string.Concat(
            host ? "    public SeedTarget Target => SeedTarget.Host;\n\n" : string.Empty,
            development != deployment ? $"    public SeedEnvironments Environments => SeedEnvironments.{(development ? "Development" : "Deployment")};\n\n" : string.Empty);
        var tenant = host ? "once per run, without a tenant" : "for every tenant, with the tenant set (context.TenantId)";
        return new GeneratedFile(System.IO.Path.Combine(project, "Seeds", type + ".cs"), $$"""
            using Coworkee.Application.Setup;
            using Nextended.Core.Attributes;

            namespace {{System.IO.Path.GetFileName(project)}}.Seeds;

            /// <summary>Runs on every migrations run, {{tenant}}, as the system with every permission.</summary>
            [RegisterAs(typeof(ISeedContributor))]
            internal sealed class {{type}} : ISeedContributor
            {
            {{members}}    public Task SeedAsync(SeedContext context, CancellationToken cancellationToken)
                {
                    // idempotent: read what exists, then add or change only what is missing; sample records only when context.IsNew
                    return Task.CompletedTask;
                }
            }

            """);
    }
}

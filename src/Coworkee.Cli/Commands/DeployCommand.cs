using System.CommandLine;

namespace Coworkee.Cli.Commands;

/// <summary>
/// Publishes the app host for a deployment target (what <c>aspire publish</c> does, without the Aspire CLI): Docker Compose with a filled
/// .env and locally built images, Azure Container Apps as Bicep, Kubernetes as a Helm chart.
/// </summary>
internal static class DeployCommand
{
    public static readonly string[] Targets = ["compose", "azure", "k8s"];

    private static readonly Argument<string> Target = new Argument<string>("target")
    {
        Description = "compose (Docker Compose on one host), azure (Azure Container Apps) or k8s (Kubernetes).",
        DefaultValueFactory = _ => "compose",
    }.AcceptOnlyFromAmong(Targets);

    private static readonly Option<string?> Output = new("--output", "-o") { Description = "Folder for the artifacts, default deploy/<target> in the solution." };

    private static readonly Option<bool> NoBuild = new("--no-build") { Description = "Do not build the container images (compose)." };

    public static Command Create() =>
        new Command("deploy", "Write the deployment artifacts of the app: Docker Compose, Azure Container Apps (Bicep) or Kubernetes (Helm).")
        {
            Target, Output, NoBuild,
        }
        .Handle((result, cancellationToken) => RunAsync(result.GetValue(Target)!, result.GetValue(Output), result.GetValue(NoBuild), cancellationToken));

    public static string OutputFolder(Solution solution, string target, string? output) =>
        Path.GetFullPath(output ?? Path.Combine(solution.Root, "deploy", target));

    public static string[] PublishArgs(Solution solution, string target, string output) =>
    [
        "dotnet", "run", "--project", solution.Project("AppHost"), "--no-launch-profile", "--",
        "--operation", "publish", "--step", "publish", "--output-path", output, "--environment", "Production", $"--Coworkee:Deploy:Target={target}",
    ];

    /// <summary>The image of a service: <c>shop-api</c> is built from <c>Shop.Api.csproj</c>.</summary>
    public static string[] BuildImageArgs(Solution solution, string image) =>
    [
        "dotnet", "publish", ServiceProject(solution, image), "-c", "Release", "-t:PublishContainer", "-p:EnableSdkContainerSupport=true",
        $"-p:ContainerRepository={image}", "-p:ContainerImageTag=latest",
    ];

    private static string ServiceProject(Solution solution, string image)
    {
        var name = $"{solution.Name}.{image[(image.IndexOf('-') + 1)..]}.csproj";
        return Directory.EnumerateFiles(solution.Src, "*.csproj", SearchOption.AllDirectories)
            .FirstOrDefault(p => string.Equals(Path.GetFileName(p), name, StringComparison.OrdinalIgnoreCase))
            ?? throw new CliException($"No project {name} below {solution.Src} for the image {image}.");
    }

    private static async Task<int> RunAsync(string target, string? output, bool noBuild, CancellationToken cancellationToken)
    {
        var solution = Solution.Find(Environment.CurrentDirectory);
        var folder = OutputFolder(solution, target, output);
        var build = target == "compose" && !noBuild;
        if (build && (await Shell.CaptureAsync("docker", "info", "--format", "{{.ServerVersion}}")).ExitCode != 0)
        {
            throw new CliException("Docker is not running: the images are built into the local Docker. Start it, or pass --no-build.");
        }

        var envPath = Path.Combine(folder, ".env");
        var previous = File.Exists(envPath) ? ComposeEnv.Values(await File.ReadAllTextAsync(envPath, cancellationToken)) : [];
        if (await Shell.RunAsync(PublishArgs(solution, target, folder), solution.Root, cancellationToken) != 0)
        {
            return 1;
        }

        return target switch
        {
            "compose" => await ComposeAsync(solution, folder, envPath, previous, build, cancellationToken),
            "azure" => NextSteps(folder, "Azure Container Apps (Bicep)", "az bicep build --file main.bicep  (checks the templates)", "azd init, then azd up (asks for the public addresses, certificates and setup token)", "after each deployment start the migrations: az containerapp job start -n <app>-migrations -g <resource group>"),
            _ => Kubernetes(folder),
        };
    }

    private static int Kubernetes(string folder)
    {
        KubernetesChart.Repair(folder);
        return NextSteps(folder, "Kubernetes (Helm chart)", "fill values.yaml: the public addresses (*_url), the secrets (connection strings, passwords, certificates, setup token) and the images", "helm lint . && helm template . | kubeconform -strict -summary",
            "build and push the images (dotnet publish -t:PublishContainer), then helm install");
    }

    private static async Task<int> ComposeAsync(Solution solution, string folder, string envPath, IReadOnlyDictionary<string, string> previous, bool build,
        CancellationToken cancellationToken)
    {
        var env = ComposeEnv.Fill(await File.ReadAllTextAsync(envPath, cancellationToken), previous, solution.Name);
        await File.WriteAllTextAsync(envPath, env, cancellationToken);
        if (build)
        {
            foreach (var image in ComposeEnv.Images(env))
            {
                if (await Shell.RunAsync(BuildImageArgs(solution, image), solution.Root, cancellationToken) != 0)
                {
                    return 1;
                }
            }
        }

        var missing = ComposeEnv.Values(env).Where(v => v.Value.Length == 0).Select(v => v.Key).ToList();
        return NextSteps(folder, "Docker Compose",
        [
            .. missing.Count > 0 ? [$"set {string.Join(", ", missing)} in .env (the public https addresses of the reverse proxy)"] : Array.Empty<string>(),
            "put a reverse proxy with HTTPS in front of the ports 127.0.0.1:8080 and up (see the hosting docs)",
            "docker compose up -d",
            $"open the web address and set the system up with {solution.Name.ToUpperInvariant()}_SETUP_TOKEN from .env",
        ]);
    }

    private static int NextSteps(string folder, string what, params string[] steps)
    {
        Console.WriteLine($"{what} written to {folder}. Next:");
        foreach (var step in steps)
        {
            Console.WriteLine($"  - {step}");
        }

        return 0;
    }
}

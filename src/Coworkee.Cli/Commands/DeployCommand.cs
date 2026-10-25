using System.CommandLine;

namespace Coworkee.Cli.Commands;

/// <summary>
/// Publishes the app host for a deployment target (what <c>aspire publish</c> does, without the Aspire CLI): Docker Compose with a filled
/// .env and locally built images, Azure Container Apps as Bicep, Kubernetes as a Helm chart. The tools the app host uses go along.
/// </summary>
internal static class DeployCommand
{
    public static readonly string[] Targets = ["compose", "azure", "k8s"];

    /// <summary>The tools of the app host (CoworkeeTools); each one is deployed when the app uses it, Mailpit only with <c>--with mailpit</c>.</summary>
    public static readonly string[] Tools = ["keycloak", "mailpit", "libretranslate", "grafana", "webdatastudio", "ollama", "aspireui", "kibana", "elsastudio"];

    private static readonly Argument<string> Target = new Argument<string>("target")
    {
        Description = "compose (Docker Compose on one host), azure (Azure Container Apps) or k8s (Kubernetes).",
        DefaultValueFactory = _ => "compose",
    }.AcceptOnlyFromAmong(Targets);

    private static readonly Option<string?> Output = new("--output", "-o") { Description = "Folder for the artifacts, default deploy/<target> in the solution." };

    private static readonly Option<bool> NoBuild = new("--no-build") { Description = "Do not build the container images (compose)." };

    private static readonly Option<string[]> Without = new Option<string[]>("--without")
    {
        Description = "Leave a tool of the app host out of the deployment, e.g. --without grafana.", AllowMultipleArgumentsPerToken = true,
    }.AcceptOnlyFromAmong(Tools);

    private static readonly Option<string[]> With = new Option<string[]>("--with")
    {
        Description = "Deploy a tool that stays out by default: --with mailpit for a test deployment without a real mail server.", AllowMultipleArgumentsPerToken = true,
    }.AcceptOnlyFromAmong(Tools);

    private static readonly Option<string?> AdminEmail = new("--admin-email") { Description = "Email of the first administrator (compose; asked for, or admin@<app>.local)." };

    public static Command Create() =>
        new Command("deploy", "Write the deployment artifacts of the app and its tools: Docker Compose, Azure Container Apps (Bicep) or Kubernetes (Helm).")
        {
            Target, Output, NoBuild, Without, With, AdminEmail,
        }
        .Handle((result, cancellationToken) => RunAsync(
            result.GetValue(Target)!, result.GetValue(Output), result.GetValue(NoBuild), ToolSwitches(result.GetValue(Without), result.GetValue(With)), result.GetValue(AdminEmail),
            cancellationToken));

    public static string OutputFolder(Solution solution, string target, string? output) =>
        Path.GetFullPath(output ?? Path.Combine(solution.Root, "deploy", target));

    public static Dictionary<string, bool> ToolSwitches(string[]? without, string[]? with) =>
        (without ?? []).Select(t => (Tool: t, Deploy: false)).Concat((with ?? []).Select(t => (Tool: t, Deploy: true))).ToDictionary(t => t.Tool, t => t.Deploy);

    public static string[] PublishArgs(Solution solution, string target, string output, IReadOnlyDictionary<string, bool>? tools = null) =>
    [
        "dotnet", "run", "--project", solution.Project("AppHost"), "--no-launch-profile", "--",
        "--operation", "publish", "--step", "publish", "--output-path", output, "--environment", "Production", $"--Coworkee:Deploy:Target={target}",
        .. (tools ?? new Dictionary<string, bool>()).Select(t => $"--Coworkee:Deploy:Tools:{t.Key}={(t.Value ? "true" : "false")}"),
    ];

    /// <summary>The image of a service: <c>shop-api</c> is built from <c>Shop.Api.csproj</c>.</summary>
    public static string[] BuildImageArgs(Solution solution, string image) =>
    [
        "dotnet", "publish", ServiceProject(solution, image) ?? throw new CliException($"No project {ProjectName(solution, image)} below {solution.Src} for the image {image}."),
        "-c", "Release", "-t:PublishContainer", "-p:EnableSdkContainerSupport=true", $"-p:ContainerRepository={image}", "-p:ContainerImageTag=latest",
    ];

    /// <summary>A tool the app host builds from a Dockerfile (Grafana with the app's dashboards, ...), which publishing writes to <c>.generated/publish/{image}</c>.</summary>
    public static string[] BuildToolImageArgs(Solution solution, string image, string tag) =>
        Directory.EnumerateFiles(solution.Src, "Dockerfile", SearchOption.AllDirectories)
            .Select(f => Path.GetDirectoryName(f)!)
            .FirstOrDefault(d => Path.GetFileName(d) == image && Path.GetFileName(Path.GetDirectoryName(d)) == "publish") is { } context
            ? ["docker", "build", "-t", tag, context]
            : throw new CliException($"No project and no Dockerfile below {solution.Src} for the image {image}.");

    private static string ProjectName(Solution solution, string image) => $"{solution.Name}.{image[(image.IndexOf('-') + 1)..]}.csproj";

    private static string? ServiceProject(Solution solution, string image) =>
        image.StartsWith($"{solution.Name.ToLowerInvariant()}-", StringComparison.Ordinal)
            ? Directory.EnumerateFiles(solution.Src, "*.csproj", SearchOption.AllDirectories)
                .FirstOrDefault(p => string.Equals(Path.GetFileName(p), ProjectName(solution, image), StringComparison.OrdinalIgnoreCase))
            : null;

    private static async Task<int> RunAsync(string target, string? output, bool noBuild, IReadOnlyDictionary<string, bool> tools, string? adminEmail,
        CancellationToken cancellationToken)
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
        if (await Shell.RunAsync(PublishArgs(solution, target, folder, tools), solution.Root, cancellationToken) != 0)
        {
            return 1;
        }

        return target switch
        {
            "compose" => await ComposeAsync(solution, folder, envPath, previous, build, adminEmail, cancellationToken),
            "azure" => NextSteps(folder, "Azure Container Apps (Bicep)", DeploySummary.PublicAddresses(folder),
                "az bicep build --file main.bicep  (checks the templates)",
                "azd init, then azd up (asks for the public addresses, the administrator, the mail server, certificates and secrets)",
                "after each deployment start the migrations: az containerapp job start -n <app>-migrations -g <resource group>"),
            _ => Kubernetes(folder),
        };
    }

    private static int Kubernetes(string folder)
    {
        KubernetesChart.Repair(folder);
        return NextSteps(folder, "Kubernetes (Helm chart)", DeploySummary.PublicAddresses(folder),
            "fill values.yaml: the public addresses (*_url), the administrator, the mail server, the secrets (connection strings, passwords, certificates) and the images",
            "helm lint . && helm template . | kubeconform -strict -summary",
            "build and push the images (dotnet publish -t:PublishContainer, docker build for the tools), then helm install");
    }

    private static async Task<int> ComposeAsync(Solution solution, string folder, string envPath, IReadOnlyDictionary<string, string> previous, bool build, string? adminEmail,
        CancellationToken cancellationToken)
    {
        var published = await File.ReadAllTextAsync(envPath, cancellationToken);
        var known = DeploySummary.Known(ComposeEnv.Values(published).Keys, previous, Environment.GetEnvironmentVariable, adminEmail, Console.IsInputRedirected ? null : Ask);
        var env = ComposeEnv.Fill(published, known, solution.Name);
        await File.WriteAllTextAsync(envPath, env, cancellationToken);
        var values = ComposeEnv.Values(env);
        if (build)
        {
            foreach (var (image, tag) in ComposeEnv.ImageTags(env))
            {
                var args = ServiceProject(solution, image) is null ? BuildToolImageArgs(solution, image, tag) : BuildImageArgs(solution, image);
                if (await Shell.RunAsync(args, solution.Root, cancellationToken) != 0)
                {
                    return 1;
                }
            }
        }

        var compose = await File.ReadAllTextAsync(Path.Combine(folder, "docker-compose.yaml"), cancellationToken);
        Console.WriteLine("Addresses (the reverse proxy forwards each public address to its loopback port):");
        foreach (var line in DeploySummary.Addresses(compose, values, solution.Name))
        {
            Console.WriteLine($"  {line}");
        }

        foreach (var line in DeploySummary.Credentials(values))
        {
            Console.WriteLine(line);
        }

        var missing = values.Where(v => v.Value.Length == 0).Select(v => v.Key).ToList();
        return NextSteps(folder, "Docker Compose", [],
        [
            .. missing.Count > 0 ? [$"set {string.Join(", ", missing)} in .env (public https addresses of the reverse proxy, the mail server)"] : Array.Empty<string>(),
            "put a reverse proxy with HTTPS in front of the loopback ports above (see the hosting docs)",
            "docker compose up -d",
        ]);
    }

    private static string? Ask(string key)
    {
        Console.Write($"{key} (empty: set it later in .env): ");
        if (!key.EndsWith("_PASSWORD", StringComparison.Ordinal))
        {
            return Console.ReadLine()?.Trim();
        }

        var secret = new System.Text.StringBuilder();
        for (var typed = Console.ReadKey(intercept: true); typed.Key != ConsoleKey.Enter; typed = Console.ReadKey(intercept: true))
        {
            if (typed.Key == ConsoleKey.Backspace && secret.Length > 0)
            {
                secret.Length--;
            }
            else if (!char.IsControl(typed.KeyChar))
            {
                secret.Append(typed.KeyChar);
            }
        }

        Console.WriteLine();
        return secret.ToString();
    }

    private static int NextSteps(string folder, string what, IReadOnlyList<string> addresses, params string[] steps)
    {
        Console.WriteLine($"{what} written to {folder}.");
        if (addresses.Count > 0)
        {
            Console.WriteLine($"Public addresses of the services and tools: {string.Join(", ", addresses)}");
        }

        Console.WriteLine("Next:");
        foreach (var step in steps)
        {
            Console.WriteLine($"  - {step}");
        }

        return 0;
    }
}

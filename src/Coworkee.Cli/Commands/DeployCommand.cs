using System.CommandLine;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Commands;

/// <summary>
/// Publishes the app host for a deployment target (what <c>aspire publish</c> does, without the Aspire CLI): Docker Compose with a filled
/// .env and locally built images, Azure Container Apps as Bicep, Kubernetes as a Helm chart. The tools the app host uses go along.
/// </summary>
internal static class DeployCommand
{
    public static readonly string[] Targets = ["compose", "azure", "k8s"];

    public static readonly string[] Tools = Deployments.Tools;

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

    private static readonly Option<string?> AdminEmail = new("--admin-email")
    {
        Description = "Email of the first administrator (compose: asked for, or admin@<app>.local; azure --apply: the signed-in Azure account).",
    };

    private static readonly Option<bool> Apply = new("--apply") { Description = "azure: deploy into --resource-group as well (Azure CLI signed in; Aspire builds, pushes and deploys)." };

    private static readonly Option<string?> ResourceGroup = new("--resource-group", "-g") { Description = "azure --apply: the resource group, made when missing; nothing is created outside it." };

    private static readonly Option<string?> Location = new("--location", "-l") { Description = "azure --apply and --github: the Azure region, e.g. germanywestcentral." };

    private static readonly Option<string?> Subscription = new("--subscription") { Description = "azure --apply: the subscription (default: the Azure CLI's)." };

    private static readonly Option<string?> Name = new("--name") { Description = "azure --apply: the name of the deployment in .coworkee/deployments (default: the resource group)." };

    private static readonly Option<bool> Yes = new("--yes", "-y") { Description = "azure --apply: do not ask before deploying." };

    private static readonly Option<bool> GitHub = new("--github") { Description = $"azure: write {GitHubDeployWorkflow.RelativePath}, which deploys every push to --branches into a resource group of its own." };

    private static readonly Option<string?> ResourceGroupPrefix = new("--resource-group-prefix") { Description = "azure --github: the resource groups are <prefix>-<branch> (empty: the app's name)." };

    private static readonly Option<string[]> Branches = new("--branches") { Description = "azure --github: the branches to deploy (default main); * matches like in GitHub.", AllowMultipleArgumentsPerToken = true };

    private static readonly Option<bool?> Teardown = new("--teardown-on-delete") { Description = "azure --github: delete a branch's resource group when the branch is deleted (true/false, default false)." };

    private static readonly Option<bool> Force = new("--force") { Description = "azure --github: overwrite a workflow that was changed by hand." };

    public static Command Create() =>
        new Command("deploy", "Write the deployment artifacts of the app and its tools: Docker Compose, Azure Container Apps (Bicep) or Kubernetes (Helm).")
        {
            Target, Output, NoBuild, Without, With, AdminEmail, Apply, ResourceGroup, Location, Subscription, Name, Yes, GitHub, ResourceGroupPrefix, Branches, Teardown, Force,
        }
        .Handle((result, cancellationToken) => result.GetValue(GitHub) ? GitHubWorkflowAsync(result, cancellationToken) : RunAsync(
            result.GetValue(Target)!, result.GetValue(Output), result.GetValue(NoBuild), ToolSwitches(result.GetValue(Without), result.GetValue(With)), result.GetValue(AdminEmail),
            AzureTarget(result), result.GetValue(Yes), cancellationToken));

    private static AzureDeploy.Target? AzureTarget(ParseResult result)
    {
        if (!result.GetValue(Apply))
        {
            return null;
        }

        if (result.GetValue(Target) != "azure")
        {
            throw new CliException("--apply deploys to Azure only: coworkee deploy azure --resource-group <name> --location <region> --apply");
        }

        var group = result.GetValue(ResourceGroup) ?? throw new CliException("--apply needs --resource-group.");
        var location = result.GetValue(Location) ?? throw new CliException("--apply needs --location, e.g. germanywestcentral.");
        return new AzureDeploy.Target(group, location, result.GetValue(Subscription), result.GetValue(Name) ?? group);
    }

    /// <summary>The settings of the app (.coworkee/app.json) with the given switches over them: the workflow is written and the settings are kept.</summary>
    private static async Task<int> GitHubWorkflowAsync(ParseResult result, CancellationToken cancellationToken)
    {
        if (result.GetValue(Target) != "azure" || result.GetValue(Apply) || result.GetValue(Subscription) is not null)
        {
            throw new CliException("--github writes the deploy workflow for Azure: coworkee deploy azure --github [--location <region>] (without --apply; the subscription is the GitHub secret AZURE_SUBSCRIPTION_ID)");
        }

        var solution = Solution.Find(Environment.CurrentDirectory);
        var (spec, _) = Apps.AppState.Read(solution);
        var saved = DeployWorkflowSettings.Of(spec);
        var settings = new DeployWorkflowSettings("azure", result.GetValue(ResourceGroupPrefix) is { } prefix ? (prefix.Length > 0 ? prefix : null) : saved.ResourceGroupPrefix, result.GetValue(Location) ?? saved.Location,
            result.GetValue(Branches) is { Length: > 0 } branches ? DeployWorkflowSettings.BranchList(string.Join(",", branches)) : saved.Branches,
            result.GetValue(Teardown) ?? saved.TeardownOnDelete);
        var path = await GitHubDeployWorkflow.WriteAsync(solution, settings, result.GetValue(Force), cancellationToken);
        settings.WriteTo(spec);
        Apps.AppState.Write(solution.Root, spec);
        return NextSteps(path, "The GitHub Actions deploy workflow", [],
            "once in Azure: an app registration with a federated credential per branch environment and its role assignments (see Hosting > Deployment in the docs)",
            $"in GitHub: secrets AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_SUBSCRIPTION_ID and COWORKEE_SECRETS_TOKEN; variable {solution.Name.ToUpperInvariant()}_ADMIN_EMAIL",
            "commit and push the workflow; the settings are kept in .coworkee/app.json");
    }

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
        AzureDeploy.Target? azure, bool yes, CancellationToken cancellationToken)
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
        // empty parameters a former deployment left in Aspire's state would make this publish wait for them
        if (azure is not null)
        {
            await AzureDeploy.PrepareStateAsync(solution, new Dictionary<string, string>(), cancellationToken);
        }

        if (await Shell.RunAsync(PublishArgs(solution, target, folder, tools), solution.Root, cancellationToken) != 0)
        {
            return 1;
        }

        return target switch
        {
            "compose" => await ComposeAsync(solution, folder, envPath, previous, build, adminEmail, cancellationToken),
            "azure" when azure is not null => await AzureDeploy.ApplyAsync(solution, folder, azure, yes, adminEmail, tools, cancellationToken),
            "azure" => NextSteps(folder, "Azure Container Apps (Bicep)", DeploySummary.PublicAddresses(folder),
                "az bicep build --file main.bicep  (checks the templates)",
                "deploy: coworkee deploy azure --resource-group <name> --location <region> --apply  (Azure CLI signed in)"),
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

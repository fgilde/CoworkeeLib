using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Commands;

/// <summary>
/// <c>coworkee deploy azure --resource-group rg --location l --apply</c>: checks the signed-in Azure CLI account, fills the parameters the app needs
/// (kept per deployment in <c>.coworkee/deployments</c>), lets Aspire's deploy step build, push and deploy into that one resource group (deployments
/// scoped to it, nothing at the subscription), runs the migrations job and writes down where the app lives.
/// </summary>
internal static partial class AzureDeploy
{
    public static readonly string Az = Deployments.AzureCli;

    public sealed record Target(string ResourceGroup, string Location, string? Subscription, string Name);

    public sealed record Account(string User, string SubscriptionId, string SubscriptionName, string TenantId);

    public static string RecordPath(Solution solution, string name) => Deployments.RecordPath(solution.Root, name);

    public static string SecretsPath(Solution solution, string name) => Deployments.SecretsPath(solution.Root, name);

    public static async Task<int> ApplyAsync(Solution solution, string published, Target target, bool yes, string? adminEmail, IReadOnlyDictionary<string, bool> tools,
        CancellationToken cancellationToken)
    {
        if (await SignedInAsync(target.Subscription) is not { } account)
        {
            throw new CliException("The Azure CLI is not signed in (or the subscription is unknown): run az login, then try again.");
        }

        Console.WriteLine($"Azure: {account.User}, subscription {account.SubscriptionName} ({account.SubscriptionId}), resource group {target.ResourceGroup} in {target.Location}");
        Console.WriteLine("Everything is created in that resource group only; nothing at the subscription.");
        if (!yes && (Console.IsInputRedirected || !Confirm("Deploy there? [y/N] ")))
        {
            Console.WriteLine("Nothing deployed (pass --yes to skip the question).");
            return 1;
        }

        // a service principal (a GitHub workflow) signs in with its client id, which is no administrator's email
        var parameters = await ParametersAsync(solution, published, target, adminEmail ?? (account.User.Contains('@') ? account.User : null), cancellationToken);
        await ForgetStaleProvisioningAsync(solution, target, account, cancellationToken);
        var environment = new Dictionary<string, string>
        {
            ["Azure__SubscriptionId"] = account.SubscriptionId,
            ["Azure__TenantId"] = account.TenantId,
            ["Azure__ResourceGroup"] = target.ResourceGroup,
            ["Azure__Location"] = target.Location,
            ["Azure__CredentialSource"] = "AzureCli",
            ["Azure__AllowResourceGroupCreation"] = "true",
        };
        foreach (var (name, value) in parameters)
        {
            environment[$"Parameters__{name}"] = value;
        }

        // the app host exits with 0 when a pipeline step fails; its output tells
        var failed = false;
        if (await Shell.RunAsync(DeployArgs(solution, tools), solution.Root, cancellationToken, environment,
                line => failed |= line.Contains("Pipeline execution failed", StringComparison.Ordinal)) != 0 || failed)
        {
            Console.Error.WriteLine("The deployment failed; see the output above.");
            return 1;
        }

        if (!await MigrateAsync(solution, target, account, cancellationToken))
        {
            return 1;
        }

        var urls = await AddressesAsync(target, account);
        await Deployments.WriteAsync(solution.Root,
            new DeploymentRecord(target.Name, "azure", account.SubscriptionId, account.TenantId, target.ResourceGroup, target.Location, account.User, DateTimeOffset.Now, urls)
            {
                Tools = tools.ToDictionary(),
            },
            cancellationToken);
        Console.WriteLine();
        Console.WriteLine($"Deployed {solution.Name} to {target.ResourceGroup}:");
        foreach (var (app, url) in urls)
        {
            Console.WriteLine($"  {app,-22} {url}");
        }

        Console.WriteLine($"Sign in as {parameters.GetValueOrDefault($"{solution.Name.ToLowerInvariant()}-admin-email")}; the password is in {SecretsPath(solution, target.Name)}.");
        return 0;
    }

    /// <summary>Aspire's deployment state of the app host (environment Production): ~/.aspire/deployments/{SHA-256 of the lower-cased project path}/production.json.</summary>
    public static string StatePath(Solution solution) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aspire", "deployments",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(solution.Project("AppHost")).ToLowerInvariant()))),
            "production.json");

    /// <summary>
    /// Aspire remembers what it provisioned (Azure:Deployments:*) and skips it next time; after the resource group was emptied, or for another group or
    /// subscription, that would point at resources that do not exist. Generated parameter values stay: an update must keep the database passwords.
    /// </summary>
    public static async Task ForgetStaleProvisioningAsync(Solution solution, Target target, Account account, CancellationToken cancellationToken)
    {
        var path = StatePath(solution);
        if (!File.Exists(path) || JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken)) is not JsonObject state)
        {
            return;
        }

        var (_, count) = await Shell.CaptureAsync(Az, "resource", "list", "-g", target.ResourceGroup, "--subscription", account.SubscriptionId, "--query", "length(@)", "-o", "tsv");
        var elsewhere = !string.Equals(state["Azure:ResourceGroup"]?.GetValue<string>(), target.ResourceGroup, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(state["Azure:SubscriptionId"]?.GetValue<string>(), account.SubscriptionId, StringComparison.OrdinalIgnoreCase);
        if (count is not ("0" or "") && !elsewhere)
        {
            return;
        }

        foreach (var key in state.Select(p => p.Key).Where(k => k.StartsWith("Azure:Deployments:", StringComparison.Ordinal)).ToList())
        {
            state.Remove(key);
        }

        await File.WriteAllTextAsync(path, state.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        Console.WriteLine("Aspire's remembered provisioning was for another or an empty resource group; everything is provisioned again.");
    }

    public static string[] DeployArgs(Solution solution, IReadOnlyDictionary<string, bool> tools) =>
    [
        "dotnet", "run", "--project", solution.Project("AppHost"), "--no-launch-profile", "--",
        "--operation", "publish", "--step", "deploy", "--environment", "Production", "--Coworkee:Deploy:Target=azure",
        .. tools.Select(t => $"--Coworkee:Deploy:Tools:{t.Key}={(t.Value ? "true" : "false")}"),
    ];

    /// <summary>The account the Azure CLI uses for <paramref name="subscription"/> (or its default); null when it is not signed in.</summary>
    public static async Task<Account?> SignedInAsync(string? subscription)
    {
        var (exit, output) = await Shell.CaptureAsync([Az, "account", "show", "-o", "json", .. subscription is null ? Array.Empty<string>() : ["--subscription", subscription]]);
        if (exit != 0 || output.Length == 0)
        {
            return null;
        }

        // az account show answers from its cache; only a token proves the sign-in still holds (the cache drops it after a while)
        var json = JsonNode.Parse(output)!;
        var token = await Shell.CaptureAsync(Az, "account", "get-access-token", "--subscription", json["id"]!.GetValue<string>(), "--query", "expiresOn", "-o", "tsv");
        if (token.ExitCode != 0)
        {
            return null;
        }

        return new Account(json["user"]?["name"]?.GetValue<string>() ?? "?", json["id"]!.GetValue<string>(), json["name"]?.GetValue<string>() ?? "?", json["tenantId"]!.GetValue<string>());
    }

    /// <summary>
    /// The parameters of the published Bicep the deployment generates (administrator, certificates, push keys, secrets, mail): kept from the last
    /// deployment, else from the environment or asked for, else generated. Parameters the app host's own configuration has stay with it.
    /// </summary>
    public static async Task<Dictionary<string, string>> ParametersAsync(Solution solution, string published, Target target, string? adminEmail, CancellationToken cancellationToken)
    {
        var app = solution.Name.ToLowerInvariant();
        var names = Directory.EnumerateFiles(published, "*.bicep", SearchOption.AllDirectories)
            .SelectMany(f => ParameterLine().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value.Replace('_', '-')))
            .Where(n => n.StartsWith($"{app}-", StringComparison.Ordinal) && Generated(n))
            .Distinct().ToList();
        var path = SecretsPath(solution, target.Name);
        var kept = File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(path, cancellationToken)) ?? [] : [];

        // the keys of the compose .env (SHOP_ADMIN_EMAIL, ...): the same environment variables and questions as coworkee deploy compose
        static string Key(string name) => name.ToUpperInvariant().Replace('-', '_');
        var known = DeploySummary.Known(names.Select(Key), kept.ToDictionary(k => Key(k.Key), k => k.Value), Environment.GetEnvironmentVariable, adminEmail,
            Console.IsInputRedirected ? null : Ask);
        var generated = ComposeEnv.Generate(names.Select(Key).Where(k => known.GetValueOrDefault(k) is not { Length: > 0 }), solution.Name);
        var values = names.ToDictionary(n => n, n => known.GetValueOrDefault(Key(n)) is { Length: > 0 } value ? value : generated.GetValueOrDefault(Key(n), string.Empty));

        // a Container Apps secret cannot be empty: without a mail server the password is a placeholder and no mail is sent
        foreach (var name in values.Keys.Where(n => n.EndsWith("-smtp-password", StringComparison.Ordinal) && values[n].Length == 0).ToList())
        {
            values[name] = "none";
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        await IgnoreSecretsAsync(solution, cancellationToken);
        return values;
    }

    private static bool Generated(string name) =>
        name.EndsWith("-admin-email", StringComparison.Ordinal) || name.EndsWith("-password", StringComparison.Ordinal) || name.EndsWith("-secret", StringComparison.Ordinal)
        || name.EndsWith("-token", StringComparison.Ordinal) || name.EndsWith("-certificate", StringComparison.Ordinal) || name.Contains("-push-", StringComparison.Ordinal)
        || name.Contains("-smtp-", StringComparison.Ordinal);

    // the generated values never go into the repository
    private static async Task IgnoreSecretsAsync(Solution solution, CancellationToken cancellationToken)
    {
        const string Line = ".coworkee/deployments/*.secrets.json";
        var gitignore = Path.Combine(solution.Root, ".gitignore");
        var text = File.Exists(gitignore) ? await File.ReadAllTextAsync(gitignore, cancellationToken) : string.Empty;
        if (!text.Contains(Line, StringComparison.Ordinal))
        {
            await File.AppendAllTextAsync(gitignore, $"{(text.Length == 0 || text.EndsWith('\n') ? string.Empty : "\n")}{Line}\n", cancellationToken);
        }
    }

    // the migrations are a Container Apps job; the services wait for the schema (the auth server keeps trying to seed its clients)
    private static async Task<bool> MigrateAsync(Solution solution, Target target, Account account, CancellationToken cancellationToken)
    {
        var job = $"{solution.Name.ToLowerInvariant()}-migrations";
        var (exit, execution) = await Shell.CaptureAsync(Az, "containerapp", "job", "start", "-n", job, "-g", target.ResourceGroup, "--subscription", account.SubscriptionId,
            "--query", "name", "-o", "tsv");
        if (exit != 0 || execution.Length == 0)
        {
            Console.Error.WriteLine($"The migrations job {job} could not be started: az containerapp job start -n {job} -g {target.ResourceGroup}");
            return false;
        }

        Console.WriteLine($"Migrations: {execution}");
        for (var waited = TimeSpan.Zero; waited < TimeSpan.FromMinutes(20); waited += TimeSpan.FromSeconds(15))
        {
            var (_, status) = await Shell.CaptureAsync(Az, "containerapp", "job", "execution", "show", "-n", job, "-g", target.ResourceGroup, "--subscription", account.SubscriptionId,
                "--job-execution-name", execution, "--query", "properties.status", "-o", "tsv");
            switch (status)
            {
                case "Succeeded":
                    Console.WriteLine("Migrations done.");
                    return true;
                case "Failed" or "Stopped" or "Degraded":
                    Console.Error.WriteLine($"The migrations ended {status}: az containerapp logs show -n {job} -g {target.ResourceGroup} --type console");
                    return false;
            }

            await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
        }

        Console.Error.WriteLine("The migrations did not finish within 20 minutes.");
        return false;
    }

    private static async Task<SortedDictionary<string, string>> AddressesAsync(Target target, Account account)
    {
        var (_, output) = await Shell.CaptureAsync(Az, "containerapp", "list", "-g", target.ResourceGroup, "--subscription", account.SubscriptionId,
            "--query", "[?properties.configuration.ingress.external].{name:name, fqdn:properties.configuration.ingress.fqdn}", "-o", "json");
        var urls = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var app in JsonNode.Parse(output.Length == 0 ? "[]" : output)!.AsArray())
        {
            urls[app!["name"]!.GetValue<string>()] = $"https://{app["fqdn"]!.GetValue<string>()}";
        }

        return urls;
    }

    private static bool Confirm(string question)
    {
        Console.Write(question);
        return Console.ReadLine()?.Trim().ToLowerInvariant() is "y" or "yes" or "j" or "ja";
    }

    private static string? Ask(string key)
    {
        Console.Write($"{key} (empty to skip): ");
        return Console.ReadLine()?.Trim();
    }

    [GeneratedRegex(@"^param ([a-z0-9_]+?)_value string", RegexOptions.Multiline)]
    private static partial Regex ParameterLine();
}

using System.Text.Json;

namespace Coworkee.Cli.Spec;

/// <summary>Where an app is deployed: <c>coworkee deploy azure --apply</c> writes it to <c>.coworkee/deployments/&lt;name&gt;.json</c>, the Studio shows and updates it.</summary>
public sealed record DeploymentRecord(
    string Name,
    string Target,
    string SubscriptionId,
    string TenantId,
    string ResourceGroup,
    string Location,
    string Account,
    DateTimeOffset DeployedAt,
    IReadOnlyDictionary<string, string> Urls)
{
    /// <summary>The tool switches of the deployment (<c>--without grafana</c> is grafana: false), so an update deploys the same tools.</summary>
    public IReadOnlyDictionary<string, bool> Tools { get; init; } = new Dictionary<string, bool>();

    /// <summary>The app signs in to Azure on its own (<see cref="Deployments.AzureConfigDir"/>), so an update uses that sign-in again.</summary>
    public bool OwnAzureSignIn { get; init; }
}

/// <summary>The deployment records of an app and the files next to them; shared by the CLI that writes them and the Studio that reads them.</summary>
public static class Deployments
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>The tools of the app host (CoworkeeTools); each one is deployed when the app uses it, Mailpit only with <c>--with mailpit</c>.</summary>
    public static readonly string[] Tools = ["keycloak", "mailpit", "libretranslate", "grafana", "webdatastudio", "ollama", "aspireui", "kibana", "openwebui", "elsastudio"];

    /// <summary>Tools that stay out of a deployment unless asked for.</summary>
    public static bool DeployedByDefault(string tool) => tool != "mailpit";

    // the full path: az.cmd started by name finds its python next to the current folder (%~dp0) and fails
    public static string AzureCli => OperatingSystem.IsWindows()
        ? (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(d => Path.Combine(d.Trim('"'), "az.cmd")).FirstOrDefault(File.Exists) ?? "az.cmd"
        : "az";

    /// <summary>The environment variable that points the Azure CLI (and everything that runs it) at another folder for its sign-in and settings.</summary>
    public const string AzureConfigDirVariable = "AZURE_CONFIG_DIR";

    /// <summary>
    /// The app's own Azure CLI folder: ~/.coworkee/azure/{folder name}-{SHA-256 of the lower-cased app folder}. It holds tokens, so it is never inside the
    /// repository; the deployment record only notes that the app uses it.
    /// </summary>
    public static string AzureConfigDir(string root)
    {
        var full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(full.ToLowerInvariant())))[..16].ToLowerInvariant();
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".coworkee", "azure", $"{Path.GetFileName(full)}-{hash}");
    }

    public static string Folder(string root) => Path.Combine(root, ".coworkee", "deployments");

    public static string RecordPath(string root, string name) => Path.Combine(Folder(root), $"{name}.json");

    /// <summary>The generated parameters (administrator, passwords, certificates); git ignored, never shown.</summary>
    public static string SecretsPath(string root, string name) => Path.Combine(Folder(root), $"{name}.secrets.json");

    /// <summary>Every deployment of the app in <paramref name="root"/>, newest first; unreadable files are skipped.</summary>
    public static IReadOnlyList<DeploymentRecord> ReadAll(string root) =>
        Directory.Exists(Folder(root))
            ? [.. Directory.EnumerateFiles(Folder(root), "*.json").Where(f => !f.EndsWith(".secrets.json", StringComparison.OrdinalIgnoreCase))
                .Select(Read).OfType<DeploymentRecord>().OrderByDescending(d => d.DeployedAt)]
            : [];

    public static DeploymentRecord? Read(string file)
    {
        try
        {
            return JsonSerializer.Deserialize<DeploymentRecord>(File.ReadAllText(file), Json) is { Name: not null, ResourceGroup: not null } record
                ? record with { Urls = record.Urls ?? new Dictionary<string, string>(), Tools = record.Tools ?? new Dictionary<string, bool>() }
                : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static async Task WriteAsync(string root, DeploymentRecord record, CancellationToken cancellationToken)
    {
        var path = RecordPath(root, record.Name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(record, Json), cancellationToken);
    }
}

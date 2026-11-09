using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Coworkee.Cli.Spec;

/// <summary>
/// The GitHub Actions deploy workflow of an app: the options deployWorkflow, deployResourceGroupPrefix, deployLocation, deployBranches and deployTeardown,
/// kept in <c>.coworkee/app.json</c> like every option, so <c>coworkee deploy azure --github</c> and the Studio regenerate <see cref="RelativePath"/> from them.
/// The file starts with a hash of what coworkee wrote; a file whose content no longer matches it was changed by hand.
/// </summary>
public sealed record DeployWorkflowSettings(string Target, string? ResourceGroupPrefix, string Location, IReadOnlyList<string> Branches, bool TeardownOnDelete)
{
    public const string RelativePath = ".github/workflows/deploy-azure.yml";

    public static readonly IReadOnlyList<string> OptionIds = ["deployWorkflow", "deployResourceGroupPrefix", "deployLocation", "deployBranches", "deployTeardown"];

    private const string StampPrefix = "# coworkee-sha256: ";

    public bool IsOn => Target != "none";

    public static DeployWorkflowSettings From(IReadOnlyDictionary<string, string?> values) => new(
        values["deployWorkflow"] ?? "none",
        values["deployResourceGroupPrefix"] is { Length: > 0 } prefix ? prefix : null,
        values["deployLocation"] is { Length: > 0 } location ? location : OptionCatalog.Find("deployLocation")!.Default!,
        BranchList(values["deployBranches"]),
        values["deployTeardown"] == "true");

    public static DeployWorkflowSettings Of(AppSpec spec) => From(SpecOptions.Resolve(spec));

    /// <summary>Branches as typed: separated by commas or spaces; none means main.</summary>
    public static IReadOnlyList<string> BranchList(string? text) =>
        (text ?? string.Empty).Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is { Length: > 0 } branches ? branches : ["main"];

    public void WriteTo(AppSpec spec)
    {
        spec.Options["deployWorkflow"] = JsonValue.Create(Target);
        spec.Options["deployResourceGroupPrefix"] = ResourceGroupPrefix is null ? null : JsonValue.Create(ResourceGroupPrefix);
        spec.Options["deployLocation"] = JsonValue.Create(Location);
        spec.Options["deployBranches"] = JsonValue.Create(string.Join(",", Branches));
        spec.Options["deployTeardown"] = JsonValue.Create(TeardownOnDelete);
    }

    /// <summary>The workflow with its hash in the first line.</summary>
    public static string Stamp(string yaml) => $"{StampPrefix}{Hash(yaml)}\n{yaml}";

    /// <summary>Whether the file is no longer what coworkee wrote: no hash line, or content that does not match it.</summary>
    public static bool IsChangedByHand(string file)
    {
        // git on Windows may check the file out with CRLF; that is no change
        file = file.Replace("\r\n", "\n", StringComparison.Ordinal);
        var newline = file.IndexOf('\n');
        return !(file.StartsWith(StampPrefix, StringComparison.Ordinal) && newline > 0 && file[StampPrefix.Length..newline] == Hash(file[(newline + 1)..]));
    }

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

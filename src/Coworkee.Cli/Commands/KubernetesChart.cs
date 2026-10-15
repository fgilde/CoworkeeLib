using System.Text.RegularExpressions;

namespace Coworkee.Cli.Commands;

/// <summary>
/// Repairs the Helm chart of Aspire's Kubernetes publisher (preview): setting names with a dot (<c>Localization.TranslationProvider</c>) become
/// <c>index</c> lookups, and the migrations, which end when they are done, become a Job that Helm runs after each install and upgrade.
/// </summary>
internal static partial class KubernetesChart
{
    private const string Deployment = "apiVersion: \"apps/v1\"\nkind: \"Deployment\"";

    public static void Repair(string chart)
    {
        foreach (var file in Directory.EnumerateFiles(Path.Combine(chart, "templates"), "*.yaml", SearchOption.AllDirectories))
        {
            File.WriteAllText(file, IndexDottedKeys(File.ReadAllText(file)));
        }

        foreach (var file in Directory.EnumerateFiles(Path.Combine(chart, "templates"), "deployment.yaml", SearchOption.AllDirectories)
                     .Where(f => Path.GetFileName(Path.GetDirectoryName(f))!.EndsWith("-migrations", StringComparison.Ordinal)))
        {
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(file)!, "job.yaml"), AsJob(File.ReadAllText(file)));
            File.Delete(file);
        }
    }

    /// <summary><c>{{ .Values.config.api.A.B }}</c> reads a key <c>A.B</c> as the path A, then B; <c>index</c> reads it as one key.</summary>
    public static string IndexDottedKeys(string template) =>
        DottedKey().Replace(template, m => $"{{{{ index .Values.{m.Groups[1].Value} \"{m.Groups[2].Value}\" }}}}");

    // ponytail: text edits on the publisher's fixed layout; a changed layout fails loudly here instead of producing a broken chart
    public static string AsJob(string deployment)
    {
        var yaml = deployment.Replace("\r\n", "\n");
        var selector = yaml.IndexOf("\n  selector:", StringComparison.Ordinal);
        if (!yaml.Contains(Deployment, StringComparison.Ordinal) || selector < 0 || !yaml.Contains("\n    spec:\n", StringComparison.Ordinal))
        {
            throw new CliException("The migrations deployment of the Helm chart has an unknown layout; leave it a Deployment or turn it into a Job by hand.");
        }

        return yaml[..selector].Replace(Deployment, "apiVersion: \"batch/v1\"\nkind: \"Job\"", StringComparison.Ordinal)
            .Replace("-deployment\"\n", "-job\"\n", StringComparison.Ordinal)
            .Replace("\nmetadata:\n", "\nmetadata:\n  annotations:\n    helm.sh/hook: \"post-install,post-upgrade\"\n    helm.sh/hook-delete-policy: \"before-hook-creation\"\n", StringComparison.Ordinal)
            .Replace("\n    spec:\n", "\n    spec:\n      restartPolicy: \"OnFailure\"\n", StringComparison.Ordinal)
            + "\n  backoffLimit: 10\n";
    }

    [GeneratedRegex(@"\{\{ \.Values\.((?:config|secrets|parameters)\.[A-Za-z0-9_]+)\.([^ }]+\.[^ }]+) \}\}")]
    private static partial Regex DottedKey();
}

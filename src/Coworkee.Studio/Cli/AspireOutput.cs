using System.Text.Json;
using System.Text.RegularExpressions;

namespace Coworkee.Studio.Cli;

public sealed record AppEndpoint(string Name, string Url);

/// <summary>What the console output of an Aspire app host tells: the dashboard login, that the app is up, and which line explains a failure.</summary>
public static partial class AspireOutput
{
    public static string? DashboardLogin(string line) => LoginLine().Match(line) is { Success: true } match ? match.Groups["url"].Value : null;

    /// <summary>The setup token (and the setup link) the app host prints while the app waits for its setup.</summary>
    public static (string Token, string? Link)? SetupToken(string line) =>
        SetupLine().Match(line) is { Success: true } match ? (match.Groups["token"].Value, match.Groups["link"].Success ? match.Groups["link"].Value : null) : null;

    public static bool IsStarted(string line) => line.Contains("Distributed application started", StringComparison.Ordinal);

    /// <summary>The header of an error log entry (<c>fail: Category[0]</c>): the message is on the next line.</summary>
    public static bool IsFailHeader(string line) => line.StartsWith("fail: ", StringComparison.Ordinal) || line.StartsWith("crit: ", StringComparison.Ordinal);

    public static bool IsError(string line) => ErrorLine().IsMatch(line) && !NoErrors().IsMatch(line);

    /// <summary>The https address of every project of the solution (Web, Api, Auth), from its launch settings; the app host itself is left out.</summary>
    public static IReadOnlyList<AppEndpoint> Endpoints(string solutionFolder)
    {
        var src = Path.Combine(solutionFolder, "src");
        if (!Directory.Exists(src))
        {
            return [];
        }

        var endpoints = new List<AppEndpoint>();
        foreach (var project in Directory.GetDirectories(src).Order(StringComparer.OrdinalIgnoreCase))
        {
            var settings = Path.Combine(project, "Properties", "launchSettings.json");
            var name = Path.GetFileName(project).Split('.')[^1];
            if (name == "AppHost" || !File.Exists(settings) || Url(File.ReadAllText(settings)) is not { } url)
            {
                continue;
            }

            endpoints.Add(new AppEndpoint(name, url));
        }

        // the web app first: that is where users sign in
        return [.. endpoints.OrderBy(e => e.Name == "Web" ? 0 : 1)];
    }

    /// <summary>The project file of the app host below <paramref name="solutionFolder"/>/src, or null.</summary>
    public static string? AppHost(string solutionFolder) => Directory.Exists(Path.Combine(solutionFolder, "src"))
        ? Directory.GetFiles(Path.Combine(solutionFolder, "src"), "*.AppHost.csproj", SearchOption.AllDirectories).FirstOrDefault()
        : null;

    /// <summary>The address of the Aspire dashboard from the launch settings of the app host (without the login token).</summary>
    public static string? Dashboard(string solutionFolder) =>
        AppHost(solutionFolder) is { } project && Path.Combine(Path.GetDirectoryName(project)!, "Properties", "launchSettings.json") is var settings && File.Exists(settings)
            ? Url(File.ReadAllText(settings))
            : null;

    private static string? Url(string launchSettings)
    {
        try
        {
            using var json = JsonDocument.Parse(launchSettings, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var urls = json.RootElement.GetProperty("profiles").EnumerateObject()
                .Select(p => p.Value.TryGetProperty("applicationUrl", out var url) ? url.GetString() : null)
                .FirstOrDefault(u => u is not null)?.Split(';');
            return urls?.FirstOrDefault(u => u.StartsWith("https://", StringComparison.Ordinal)) ?? urls?.FirstOrDefault();
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"Setup token of \S+: (?<token>\S+)(?: - open (?<link>https?://\S+))?")]
    private static partial Regex SetupLine();

    [GeneratedRegex(@"Login to the dashboard at (?<url>https?://\S+)")]
    private static partial Regex LoginLine();

    [GeneratedRegex(@"\berror\b|exception|failed|address already in use", RegexOptions.IgnoreCase)]
    private static partial Regex ErrorLine();

    [GeneratedRegex(@"\b0 Error\(s\)|\b0 errors?\b", RegexOptions.IgnoreCase)]
    private static partial Regex NoErrors();
}

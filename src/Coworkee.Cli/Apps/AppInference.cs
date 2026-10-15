using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Apps;

/// <summary>
/// The options of an app created before <c>.coworkee/app.json</c> existed, read from what the template wrote:
/// package references, the app host, the infrastructure module and the seed. Options it cannot see keep their default.
/// </summary>
// ponytail: regexes over the template's own lines; an app that rewrote them reads as the defaults, the 3-way merge then lists the conflicts
internal static class AppInference
{
    public static AppSpec Infer(Solution solution)
    {
        var src = solution.Src;
        var name = solution.Name;
        var appHost = Read(Path.Combine(src, $"{name}.AppHost", "AppHost.cs"));
        var infrastructure = Read(Path.Combine(src, $"{name}.Infrastructure", $"{name}InfrastructureModule.cs"));
        var seed = Read(Path.Combine(src, $"{name}.Migrations", "DemoSeed.cs"));
        var projects = Directory.Exists(src)
            ? string.Concat(Directory.EnumerateFiles(src, "*.csproj", SearchOption.AllDirectories).Select(File.ReadAllText))
            : string.Empty;

        var values = new Dictionary<string, string?>
        {
            ["samples"] = Flag(solution.HasSamples),
            ["tests"] = Flag(Directory.Exists(Path.Combine(solution.Root, "tests"))),
            ["githubActions"] = Flag(File.Exists(Path.Combine(solution.Root, ".github", "workflows", "ci.yml"))),
            ["devcontainer"] = Flag(Directory.Exists(Path.Combine(solution.Root, ".devcontainer"))),
            ["git"] = Flag(Directory.Exists(Path.Combine(solution.Root, ".git"))),
            ["airworks"] = Flag(appHost.Contains("AddAirWorks<", StringComparison.Ordinal)),
            ["airworksPackages"] = Directory.Exists(Path.Combine(src, $"{name}.AirWorks", "ClientApp", "vendor")) ? "bundled" : "npm",
            ["eventBus"] = Has(projects, "Coworkee.EventBus.RabbitMq") ? "rabbitMq" : Has(projects, "Coworkee.EventBus") ? "inProcess" : "none",
            ["storage"] = appHost.Contains("AddAzureStorage(", StringComparison.Ordinal) ? "azureBlob" : "fileSystem",
            ["keycloak"] = Flag(appHost.Contains("options.UseKeycloak(", StringComparison.Ordinal)),
            ["redis"] = Flag(!appHost.Contains("options.WithoutRedis();", StringComparison.Ordinal)),
            ["grafana"] = Flag(appHost.Contains("options.UseGrafana();", StringComparison.Ordinal)),
            ["webDataStudio"] = Flag(appHost.Contains("options.UseWebDataStudio();", StringComparison.Ordinal)),
            ["aspireUI"] = Flag(appHost.Contains("AddAspireUI()", StringComparison.Ordinal)),
            ["database"] = appHost.Contains("options.UseSqlServer();", StringComparison.Ordinal) ? "sqlServer" : appHost.Contains("options.UseSqlite();", StringComparison.Ordinal) ? "sqlite" : "postgres",
            ["title"] = Match(appHost, @"options\.DisplayName = ""([^""]*)"";"),
            ["logo"] = Match(appHost, @"options\.LogoUrl = ""/([^""]*)"";"),
            ["aiProvider"] = Match(appHost, @"Defaults\[""Ai\.Provider""\], ""(\w+)""\)"),
            ["aiModel"] = Match(appHost, @"string aiModel = ""([^""]*)"""),
            ["aiEndpoint"] = Match(appHost, @"aiEndpoint = ""([^""]*)"";"),
            ["translationProvider"] = appHost.Contains("UseLibreTranslate(", StringComparison.Ordinal) ? "libreTranslate" : Match(appHost, @"UseTranslation\(""(\w+)"""),
            ["translationSource"] = Match(appHost, @"options\.TranslationSource = ""([^""]*)"";"),
            ["signInLayout"] = Match(appHost, @"options\.SignIn\.Layout = ""(\w+)"";"),
            ["signInMediaSide"] = Match(appHost, @"SignInMediaSide\.(\w+);"),
            ["signInMedia"] = Match(appHost, @"options\.SignIn\.MediaUrl = ""([^""]*)"";"),
            ["loadingScreen"] = Match(appHost, @"LoadingScreenBackground\.(\w+);"),
            ["primaryColor"] = Match(infrastructure, @"\[""Primary""\] = ""(#\w+)"""),
            ["secondaryColor"] = Match(infrastructure, @"\[""Secondary""\] = ""(#\w+)"""),
            ["theme"] = Match(infrastructure, @"new Coworkee\.Theming\.AppTheme\(""[^""]*"", ""(\w+)""") ?? Match(infrastructure, @"themes\.DefaultTheme = ""(\w+)"";"),
            ["languages"] = Match(infrastructure, @"localization\.Languages = \[([^\]]*)\];")?.Replace("\"", string.Empty).Replace(" ", string.Empty),
            ["defaultLanguage"] = Match(infrastructure, @"localization\.DefaultLanguage = ""([^""]*)"";"),
            ["colorMode"] = Match(seed, @"Settings\[""Theme\.Mode""\] = ""(\w+)"";"),
            ["registration"] = Flag(seed.Contains("Account.AllowRegistration", StringComparison.Ordinal)),
            ["repository"] = Match(Read(Path.Combine(solution.Root, "README.md")), @"quickrun\.org/run\?repo=([^)\s]+)\)"),
        };

        foreach (var option in OptionCatalog.All.Where(o => o is { Kind: OptionKind.Bool, Package: not null } && !values.ContainsKey(o.Id)))
        {
            values[option.Id] = Flag(Has(projects, option.Package!));
        }

        // the samples bring the extended attributes anyway, so their package says nothing then
        if (solution.HasSamples)
        {
            values["extendedAttributes"] = "false";
        }

        values["clientEntities"] = Flag(Has(projects, "Coworkee.Client.Blazor.ClientEntities"));
        if (values["title"] == name)
        {
            values["title"] = null;
        }

        var spec = new AppSpec { Name = name };
        foreach (var option in OptionCatalog.All)
        {
            if (values.GetValueOrDefault(option.Id) is { } value && value != option.Default)
            {
                spec.Options[option.Id] = option.Kind == OptionKind.Bool ? JsonValue.Create(value == "true") : JsonValue.Create(value);
            }
        }

        return spec;
    }

    private static string Flag(bool value) => value ? "true" : "false";

    private static bool Has(string projects, string package) => projects.Contains($"Include=\"{package}\"", StringComparison.Ordinal);

    private static string? Match(string text, string pattern) => Regex.Match(text, pattern) is { Success: true } match && match.Groups[1].Length > 0 ? match.Groups[1].Value : null;

    private static string Read(string path) => File.Exists(path) ? File.ReadAllText(path) : string.Empty;
}

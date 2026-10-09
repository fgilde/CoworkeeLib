using System.CommandLine;

namespace Coworkee.Cli.Commands;

internal static class NewCommand
{
    private const int AlreadyInstalled = 106;

    private static readonly Argument<string> Name = Names.Simple("name", "Name of the app, used for projects and namespaces.");
    private static readonly Option<string?> Output = new("--output", "-o") { Description = "Target folder (default: ./<name>)." };
    private static readonly Option<bool> Samples = new("--samples") { Description = "Include the Catalog and Documents sample modules.", DefaultValueFactory = _ => true };
    private static readonly Option<bool> NoSamples = new("--no-samples") { Description = "Leave out the sample modules." };
    private static readonly Option<bool> Keycloak = new("--keycloak") { Description = "Add Keycloak as external sign-in." };
    private static readonly Option<bool> NoTests = new("--no-tests") { Description = "Leave out the test projects." };
    private static readonly Option<string?> Title = new("--title") { Description = "Display title (default: the name)." };
    private static readonly Option<string?> TemplateSource = new("--template-source") { Description = "Install the template from this .nupkg or folder instead of nuget.org." };

    public static Command Create() =>
        new Command("new", "Create a Coworkee application.") { Name, Output, Samples, NoSamples, Keycloak, NoTests, Title, TemplateSource }.Handle(RunAsync);

    public static NewSettings Read(ParseResult result) => new(
        result.GetValue(Name)!,
        result.GetValue(Output) ?? result.GetValue(Name)!,
        result.GetValue(Samples) && !result.GetValue(NoSamples),
        result.GetValue(Keycloak),
        !result.GetValue(NoTests),
        result.GetValue(Title),
        result.GetValue(TemplateSource));

    public static string[] InstallArgs(NewSettings settings, string version) => settings.TemplateSource is { } source
        ? ["dotnet", "new", "install", Path.GetFullPath(source), "--force"]
        : ["dotnet", "new", "install", $"Coworkee.Templates@{version}"];

    public static string[] TemplateArgs(NewSettings settings) =>
    [
        "dotnet", "new", "coworkee", "-n", settings.Name, "-o", settings.Output,
        "--samples", Flag(settings.Samples), "--keycloak", Flag(settings.Keycloak), "--tests", Flag(settings.Tests),
        .. settings.Title is { } title ? new[] { "--title", title } : [],
    ];

    private static string Flag(bool value) => value ? "true" : "false";

    private static async Task<int> RunAsync(ParseResult result, CancellationToken cancellationToken)
    {
        var settings = Read(result);
        var installed = await Shell.RunAsync(InstallArgs(settings, CliApp.Version), cancellationToken: cancellationToken);
        if (installed != 0 && installed != AlreadyInstalled)
        {
            return installed;
        }

        var created = await Shell.RunAsync(TemplateArgs(settings), cancellationToken: cancellationToken);
        if (created != 0)
        {
            return created;
        }

        var restored = await Shell.RunAsync(["dotnet", "restore"], settings.Output, cancellationToken);
        if (restored != 0)
        {
            return restored;
        }

        Console.WriteLine();
        Console.WriteLine("Next steps:");
        Console.WriteLine($"  cd {settings.Output}");
        Console.WriteLine("  coworkee run");
        Console.WriteLine($"Sign in as admin@{settings.Name.ToLowerInvariant()}.local, the password is in src/{settings.Name}.Migrations/DemoSeed.cs.");
        return 0;
    }
}

internal sealed record NewSettings(string Name, string Output, bool Samples, bool Keycloak, bool Tests, string? Title, string? TemplateSource);

using CommunityToolkit.Mvvm.ComponentModel;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Cli;
using Coworkee.Studio.Localization;
using Coworkee.Studio.Model;

namespace Coworkee.Studio.ViewModels;

public sealed record ReviewItem(string Label, string Value);

public sealed partial class ReviewStep(MainViewModel main) : StepViewModel(main)
{
    protected override string TitleText => "Review";

    protected override string SubtitleText => "Everything in one place: the spec, its validation and the CLI command that creates the app.";

    protected override string PitchText => "The spec is the same JSON the CLI and the MCP server take. Save it next to your code, share it, or create the app right away.";

    public override string Icon => "Icon.Eye";

    [ObservableProperty]
    public partial string SpecText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FlagsCommand { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SpecCommand { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<ReviewItem> Items { get; set; } = [];

    [ObservableProperty]
    public partial SolutionSummary Summary { get; set; } = new([], [], []);

    public bool HasModules => Main.Entities.Modules.Count > 0;

    public override void Refresh()
    {
        var spec = Main.ToSpec();
        // as it is saved: next to the spec file, or, before the first save, next to the app folder
        SpecText = SpecJson.Write(Main.ToSpec(Main.SpecPath is { } path ? Path.GetDirectoryName(path) : Main.Basics.Location));
        FlagsCommand = CommandLine.Format(CommandLine.Flags(spec));
        SpecCommand = CommandLine.Format(["coworkee", "new", "--spec", Main.SpecPath ?? $"{(spec.Name.Length > 0 ? spec.Name : "app")}.json",
            .. string.IsNullOrWhiteSpace(Main.Settings.TemplateSource) ? Array.Empty<string>() : ["--template-source", Main.Settings.TemplateSource]]);
        Summary = SolutionSummary.For(spec);
        var values = SpecOptions.Resolve(spec);
        Items =
        [
            new(L.Get("Name"), spec.Name),
            new(L.Get("Title"), values["title"] ?? spec.Name),
            new(L.Get("Folder"), spec.Output ?? "-"),
            new(L.Get("Theme"), values["theme"] + (values["primaryColor"] is { } primary ? $" · {primary} / {values["secondaryColor"] ?? "-"}" : string.Empty)),
            new(L.Get("Logo"), values["logo"] is { } logo ? Path.GetFileName(logo) : "-"),
            new(L.Get("Sign-in"), $"{values["signInLayout"]}{(values["signInMedia"] is { } media ? $" · {Path.GetFileName(media)} ({values["signInMediaSide"]})" : string.Empty)}"),
            new(L.Get("Modules"), string.Join(", ", OptionCatalog.All.Where(o => o.Group == OptionCatalog.Modules && o.Kind == OptionKind.Bool && values[o.Id] == "true").Select(o => L.Get(o.Title))) is { Length: > 0 } on ? on : "-"),
            new(L.Get("Event bus"), values["eventBus"] ?? "none"),
            new(L.Get("Storage"), values["storage"] ?? "fileSystem"),
            new(L.Get("Database"), values["database"] ?? "postgres"),
            new(L.Get("AI provider"), values["ai"] == "true" ? values["aiProvider"] ?? "anthropic" : "-"),
            new(L.Get("Redis"), YesNo(values["redis"])),
            new(L.Get("Grafana"), YesNo(values["grafana"])),
            new(L.Get("WebDataStudio"), YesNo(values["webDataStudio"])),
            new(L.Get("Containers"), string.Join(", ", Summary.Containers)),
            new(L.Get("Languages"), $"{values["languages"] ?? string.Join(",", OptionCatalog.Languages.Select(l => l.Value))} · {values["defaultLanguage"]}"),
            new(L.Get("Color mode"), values["colorMode"] ?? "system"),
            new(L.Get("Self registration"), YesNo(values["registration"])),
            new(L.Get("Tooling"), string.Join(", ", OptionCatalog.All.Where(o => o.Group == OptionCatalog.Tooling && values[o.Id] == "true").Select(o => L.Get(o.Title))) is { Length: > 0 } tools ? tools : "-"),
            new(L.Get("Entities"), string.Join(", ", spec.Modules.Select(m => $"{m.Name} ({string.Join(", ", m.Entities.Select(e => e.Name))})")) is { Length: > 0 } entities ? entities : "-"),
            new(L.Get("Enums"), spec.Enums.Count > 0 ? string.Join(", ", spec.Enums.Select(e => e.Name)) : "-"),
        ];
        OnPropertyChanged(nameof(HasModules));
    }

    public override void OnShown() => Refresh();

    private static string YesNo(string? value) => L.Get(value == "true" ? "Yes" : "No");
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Localization;
using Coworkee.Studio.Model;

namespace Coworkee.Studio.ViewModels;

public sealed partial class PresetViewModel(Preset preset) : ObservableObject
{
    public Preset Preset => preset;

    public string Name => L.Get(preset.Name);

    public string Description => L.Get(preset.Description);

    public string Icon => preset.Icon;

    [ObservableProperty]
    public partial bool IsActive { get; set; }
}

public sealed partial class ArchitectureStep : StepViewModel
{
    private static readonly (string Group, string Description, string Icon)[] GroupInfo =
    [
        (OptionCatalog.General, "What the solution starts with.", "Icon.Cube"),
        (OptionCatalog.Modules, "Feature modules: each is a NuGet package with its pages, permissions and tables.", "Icon.Puzzle"),
        (OptionCatalog.Infrastructure, "Containers and services the Aspire app host starts next to your app.", "Icon.Server"),
        (OptionCatalog.Code, "How the CLI generates the code of your entities.", "Icon.Code"),
        (OptionCatalog.Tooling, "Files next to the code: a CI workflow and a dev container.", "Icon.Console"),
    ];

    public ArchitectureStep(MainViewModel main)
        : base(main)
    {
        Presets = [.. Preset.All.Select(p => new PresetViewModel(p))];
        Groups = [.. GroupInfo.Select(g => new OptionGroupViewModel(g.Group, g.Description, g.Icon,
            [.. main.Options.Values.Where(o => o.Option.Group == g.Group && o.Id != "title")]))];
    }

    protected override string TitleText => "Architecture";

    protected override string SubtitleText => "App type, modules and infrastructure, with what each choice brings and costs.";

    protected override string PitchText => "Start from a preset, then fine-tune. Every module is a package of its own; the Aspire app host starts exactly the containers your choices need.";

    public override string Icon => "Icon.Layers";

    public IReadOnlyList<PresetViewModel> Presets { get; }

    public IReadOnlyList<OptionGroupViewModel> Groups { get; }

    [ObservableProperty]
    public partial SolutionSummary Summary { get; set; } = new([], [], []);

    public string SummaryHeadline => L.Format("{0} projects · {1} containers · {2} packages", Summary.Projects.Count, Summary.Containers.Count, Summary.Packages.Count);

    public override bool Owns(string error) =>
        error.StartsWith("options.", StringComparison.Ordinal) || Groups.SelectMany(g => g.Options).Any(o => error.StartsWith(o.Id + ":", StringComparison.Ordinal));

    [RelayCommand]
    public void ApplyPreset(PresetViewModel preset)
    {
        foreach (var (id, value) in preset.Preset.Values)
        {
            Main.Option(id).Value = value;
        }
    }

    [RelayCommand]
    public void ResetOptions()
    {
        foreach (var option in Groups.SelectMany(g => g.Options))
        {
            option.Reset();
        }
    }

    public override void Refresh()
    {
        Summary = SolutionSummary.For(Main.ToSpec());
        OnPropertyChanged(nameof(SummaryHeadline));
        foreach (var preset in Presets)
        {
            preset.IsActive = preset.Preset.Values.All(v => Main.Option(v.Key).Value == v.Value);
        }
    }
}

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

    private static readonly string[] Highlighted = ["database", "redis", "grafana", "webDataStudio"];

    private static readonly string[] AiOptions = ["aiProvider", "aiModel", "aiEndpoint"];

    public ArchitectureStep(MainViewModel main)
        : base(main)
    {
        Presets = [.. Preset.All.Select(p => new PresetViewModel(p))];
        Infrastructure = [.. Highlighted.Select(main.Option)];
        Ai = new AiSetupViewModel(main);
        Groups = [.. GroupInfo.Select(g => new OptionGroupViewModel(g.Group, g.Description, g.Icon,
            [.. main.Options.Values.Where(o => o.Option.Group == g.Group && o.Id != "title" && !Highlighted.Contains(o.Id) && !LanguagesStep.Ids.Contains(o.Id) && !AiOptions.Contains(o.Id))]))];
    }

    protected override string TitleText => "Architecture";

    protected override string SubtitleText => "App type, modules and infrastructure, with what each choice brings and costs.";

    protected override string PitchText => "Start from a preset, then fine-tune. Every module is a package of its own; the Aspire app host starts exactly the containers your choices need.";

    public override string Icon => "Icon.Layers";

    public IReadOnlyList<PresetViewModel> Presets { get; }

    public IReadOnlyList<OptionGroupViewModel> Groups { get; }

    /// <summary>Database, Redis and the monitoring tools: the choices that decide how the app runs, shown first.</summary>
    public IReadOnlyList<OptionViewModel> Infrastructure { get; }

    /// <summary>Provider, model, endpoint and key of the AI assistant.</summary>
    public AiSetupViewModel Ai { get; }

    [ObservableProperty]
    public partial SolutionSummary Summary { get; set; } = new([], [], []);

    public string SummaryHeadline => L.Format("{0} projects · {1} containers · {2} packages", Summary.Projects.Count, Summary.Containers.Count, Summary.Packages.Count);

    public override bool Owns(string error) =>
        error.StartsWith("options.", StringComparison.Ordinal)
        || Groups.SelectMany(g => g.Options).Select(o => o.Id).Concat(AiOptions).Any(id => error.StartsWith(id + ":", StringComparison.Ordinal));

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
        ExplainInfrastructure();
        Ai.Refresh();
        foreach (var preset in Presets)
        {
            preset.IsActive = preset.Preset.Values.All(v => Main.Option(v.Key).Value == v.Value);
        }
    }

    private void ExplainInfrastructure()
    {
        var database = Main.Option("database").Value;
        Main.Option("database").Note = database switch
        {
            "sqlServer" => "SQL Server container (about 2 GB of RAM). No backups from the admin pages: use those of SQL Server or Azure SQL.",
            "sqlite" => "No container: one file under .data. One writer at a time, background jobs kept in memory and no backups. For demos and one machine.",
            _ => "PostgreSQL container. Every module works, backups and restore from the admin pages included.",
        };

        var backup = Main.Option("backup");
        backup.IsAvailable = database == "postgres";
        backup.Note = backup.IsAvailable ? null : "Not part of the app: backups from the admin pages need PostgreSQL.";

        var provider = Main.Option("aiProvider");
        provider.IsAvailable = Main.Option("ai").IsOn;
        provider.Note = !provider.IsAvailable ? "Only used with the AI assistant module; switch it on under Modules."
            : provider.Value switch
            {
                "ollama" => "Adds an Ollama container that pulls the model (several GB) on the first start; no key needed.",
                "lmStudio" => "LM Studio runs on this machine: start its server and load a model; no key needed.",
                "azureOpenAI" or "openAICompatible" => "Endpoint and model here or in the user secrets of the app host (Parameters:ai-endpoint, ai-model); the key in Parameters:ai-apikey.",
                _ => "Key in the user secrets of the app host (Parameters:ai-apikey) or later on the AI page of the app.",
            };

        Main.Option("redis").Note = Main.Option("redis").IsOn
            ? "Ready to scale out: several api and web instances share realtime messages, presence, caches and sessions."
            : "Each process keeps its own state: fine for one instance, but realtime messages and sessions do not cross instances.";
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Localization;

namespace Coworkee.Studio.ViewModels;

public sealed record ChoiceViewModel(string Value, string Description)
{
    private static readonly Dictionary<string, (string Label, string Icon)> Looks = new()
    {
        ["postgres"] = ("PostgreSQL", "Icon.Database"),
        ["sqlServer"] = ("SQL Server", "Icon.Server"),
        ["sqlite"] = ("SQLite", "Icon.File"),
        ["anthropic"] = ("Anthropic", "Icon.Sparkle"),
        ["openAI"] = ("OpenAI", "Icon.Robot"),
        ["azureOpenAI"] = ("Azure OpenAI", "Icon.Cloud"),
        ["ollama"] = ("Ollama", "Icon.Monitor"),
        ["openAICompatible"] = ("OpenAI compatible", "Icon.Link"),
    };

    public string Text => L.Get(Description);

    public string Label => Looks.TryGetValue(Value, out var look) ? L.Get(look.Label) : Value;

    public string Icon => Looks.TryGetValue(Value, out var look) ? look.Icon : "Icon.Cube";
}

/// <summary>One choice of a <see cref="OptionKind.Multi"/> option as a check box; none checked is the default (all).</summary>
public sealed partial class MultiChoiceViewModel(OptionViewModel option, ChoiceViewModel choice) : ObservableObject
{
    public string Value => choice.Value;

    public string Text => choice.Text;

    public bool IsChecked
    {
        get => option.Value is { } value && SpecOptions.List(value).Contains(choice.Value);
        set => option.Toggle(choice.Value, value);
    }

    public void Refresh() => OnPropertyChanged(nameof(IsChecked));
}

/// <summary>One option of the catalog with its value as the CLI spells it ("true", a choice, a color, a path or null).</summary>
public sealed partial class OptionViewModel : ObservableObject
{
    public OptionViewModel(CliOption option)
    {
        Option = option;
        Choices = option.Choices?.Select(c => new ChoiceViewModel(c.Value, c.Description)).ToList() ?? [];
        MultiChoices = option.Kind == OptionKind.Multi ? [.. Choices.Select(c => new MultiChoiceViewModel(this, c))] : [];
        Value = option.Default;
    }

    public event Action? Changed;

    public CliOption Option { get; }

    [ObservableProperty]
    public partial string? Value { get; set; }

    /// <summary>False while another choice rules the option out (backups off Postgres, a provider without the AI assistant).</summary>
    [ObservableProperty]
    public partial bool IsAvailable { get; set; } = true;

    /// <summary>What the current value means for the solution (English, the key of <see cref="NoteText"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote), nameof(NoteText))]
    public partial string? Note { get; set; }

    public bool HasNote => Note is not null;

    public string? NoteText => Note is null ? null : L.Get(Note);

    public string Id => Option.Id;

    public string Title => L.Get(Option.Title);

    public string Description => L.Get(Option.Description);

    public string WhenToUse => L.Get(Option.WhenToUse);

    public IReadOnlyList<string> Pros => [.. Option.Pros.Where(p => p != "None").Select(L.Get)];

    public IReadOnlyList<string> Cons => [.. Option.Cons.Where(c => c != "None").Select(L.Get)];

    public bool HasPros => Pros.Count > 0;

    public bool HasCons => Cons.Count > 0;

    public string Flag => Option.Flag;

    public string? Package => Option.Package;

    public bool HasPackage => Option.Package is not null;

    public string DefaultText => L.Format("Default: {0}", Option.Default ?? "-");

    public bool IsBool => Option.Kind == OptionKind.Bool;

    public bool IsChoice => Option.Kind == OptionKind.Choice;

    public bool IsMulti => Option.Kind == OptionKind.Multi;

    public IReadOnlyList<ChoiceViewModel> Choices { get; }

    public IReadOnlyList<MultiChoiceViewModel> MultiChoices { get; }

    /// <summary>Checks or unchecks a choice of a multi option, keeping the catalog order.</summary>
    public void Toggle(string choice, bool on)
    {
        var selected = (Value is { } value ? SpecOptions.List(value) : []).Where(c => c != choice).Concat(on ? [choice] : []).ToHashSet();
        var ordered = Choices.Select(c => c.Value).Where(selected.Contains).ToList();
        Value = ordered.Count == 0 ? null : string.Join(",", ordered);
    }

    public bool IsOn
    {
        get => Value == "true";
        set => Value = value ? "true" : "false";
    }

    public ChoiceViewModel? Choice
    {
        get => Choices.FirstOrDefault(c => c.Value == Value);
        set
        {
            if (value is not null)
            {
                Value = value.Value;
            }
        }
    }

    public bool IsChanged => Value != Option.Default;

    public void Reset() => Value = Option.Default;

    partial void OnValueChanged(string? value)
    {
        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(Choice));
        OnPropertyChanged(nameof(IsChanged));
        foreach (var choice in MultiChoices)
        {
            choice.Refresh();
        }

        Changed?.Invoke();
    }
}

public sealed record OptionGroupViewModel(string Name, string Description, string Icon, IReadOnlyList<OptionViewModel> Options)
{
    public string Title => L.Get(Name);

    public string Text => L.Get(Description);
}

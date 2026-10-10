using CommunityToolkit.Mvvm.ComponentModel;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Localization;

namespace Coworkee.Studio.ViewModels;

public sealed record ChoiceViewModel(string Value, string Description)
{
    public string Text => L.Get(Description);
}

/// <summary>One option of the catalog with its value as the CLI spells it ("true", a choice, a color, a path or null).</summary>
public sealed partial class OptionViewModel : ObservableObject
{
    public OptionViewModel(CliOption option)
    {
        Option = option;
        Value = option.Default;
        Choices = option.Choices?.Select(c => new ChoiceViewModel(c.Value, c.Description)).ToList() ?? [];
    }

    public event Action? Changed;

    public CliOption Option { get; }

    [ObservableProperty]
    public partial string? Value { get; set; }

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

    public IReadOnlyList<ChoiceViewModel> Choices { get; }

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
        Changed?.Invoke();
    }
}

public sealed record OptionGroupViewModel(string Name, string Description, string Icon, IReadOnlyList<OptionViewModel> Options)
{
    public string Title => L.Get(Name);

    public string Text => L.Get(Description);
}

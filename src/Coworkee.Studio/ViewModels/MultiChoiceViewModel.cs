using CommunityToolkit.Mvvm.ComponentModel;
using Coworkee.Cli.Spec;

namespace Coworkee.Studio.ViewModels;

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

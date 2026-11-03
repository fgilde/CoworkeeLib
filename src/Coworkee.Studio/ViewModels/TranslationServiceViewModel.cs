using CommunityToolkit.Mvvm.ComponentModel;
using Coworkee.Studio.Localization;

namespace Coworkee.Studio.ViewModels;

/// <summary>A translation service as a card: what it costs, where the texts go and what it needs.</summary>
public sealed partial class TranslationServiceViewModel(OptionViewModel option, ChoiceViewModel choice, IReadOnlyList<string> pros, IReadOnlyList<string> cons) : ObservableObject
{
    public string Value => choice.Value;

    public string Label => choice.Label;

    public string Icon => choice.Icon;

    public string Text => choice.Text;

    public IReadOnlyList<string> Pros => [.. pros.Select(L.Get)];

    public IReadOnlyList<string> Cons => [.. cons.Select(L.Get)];

    public bool IsSelected => option.Value == Value;

    public void Select() => option.Value = Value;

    public void Refresh() => OnPropertyChanged(string.Empty);
}

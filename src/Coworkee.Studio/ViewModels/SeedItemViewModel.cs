using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Coworkee.Studio.ViewModels;

/// <summary>Base of roles and users: their validation messages and check boxes that follow what the app offers.</summary>
public abstract partial class SeedItemViewModel(Action changed) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrors))]
    public partial IReadOnlyList<string> Errors { get; set; } = [];

    public bool HasErrors => Errors.Count > 0;

    /// <summary>Offers <paramref name="available"/> with <paramref name="checkedNames"/> (default: the checked ones) checked; untouched when nothing changed.</summary>
    protected void Offer(ObservableCollection<Choice> choices, IReadOnlyList<string> available, IEnumerable<string>? checkedNames)
    {
        if (checkedNames is null && choices.Select(c => c.Name).SequenceEqual(available))
        {
            return;
        }

        var current = (checkedNames ?? choices.Where(c => c.IsChecked).Select(c => c.Name)).ToHashSet(StringComparer.Ordinal);
        choices.Clear();
        foreach (var name in available.Union(current))
        {
            choices.Add(new Choice(name, current.Contains(name), changed));
        }
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not (nameof(Errors) or nameof(HasErrors)))
        {
            changed();
        }
    }
}

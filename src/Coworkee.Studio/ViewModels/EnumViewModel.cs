using CommunityToolkit.Mvvm.ComponentModel;
using Coworkee.Cli.Spec;

namespace Coworkee.Studio.ViewModels;

/// <summary>An enum of the spec; the values are typed comma separated.</summary>
public sealed partial class EnumViewModel(Action changed) : ObservableObject
{
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Values { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrors))]
    public partial IReadOnlyList<string> Errors { get; set; } = [];

    public bool HasErrors => Errors.Count > 0;

    public EnumSpec ToSpec() => new() { Name = Name.Trim(), Values = [.. Values.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)] };

    public static EnumViewModel From(EnumSpec spec, Action changed) => new(changed) { Name = spec.Name, Values = string.Join(", ", spec.Values) };

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not (nameof(Errors) or nameof(HasErrors)))
        {
            changed();
        }
    }
}

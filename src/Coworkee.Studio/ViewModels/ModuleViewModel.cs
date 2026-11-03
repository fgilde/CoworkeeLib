using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Localization;

namespace Coworkee.Studio.ViewModels;

public sealed partial class ModuleViewModel : ObservableObject
{
    private readonly Action _changed;

    public ModuleViewModel(Action changed)
    {
        _changed = changed;
        Entities.CollectionChanged += (_, _) => changed();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Display))]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrors))]
    public partial IReadOnlyList<string> Errors { get; set; } = [];

    public bool HasErrors => Errors.Count > 0;

    public ObservableCollection<EntityViewModel> Entities { get; } = [];

    public string Display => Name.Length > 0 ? Name : L.Get("(unnamed)");

    public ModuleSpec ToSpec() => new() { Name = Name.Trim(), Entities = [.. Entities.Select(e => e.ToSpec())] };

    public static ModuleViewModel From(ModuleSpec spec, Action changed)
    {
        var module = new ModuleViewModel(changed) { Name = spec.Name };
        foreach (var entity in spec.Entities)
        {
            module.Entities.Add(EntityViewModel.From(entity, changed));
        }

        return module;
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not (nameof(Errors) or nameof(HasErrors)))
        {
            _changed();
        }
    }
}

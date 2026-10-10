using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Localization;

namespace Coworkee.Studio.ViewModels;

public sealed partial class PropertyViewModel(Action changed) : ObservableObject
{
    public static IReadOnlyList<PropertyType> Types { get; } = Enum.GetValues<PropertyType>();

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsText))]
    public partial PropertyType Type { get; set; }

    [ObservableProperty]
    public partial bool Required { get; set; }

    [ObservableProperty]
    public partial decimal? MaxLength { get; set; }

    public bool IsText => Type == PropertyType.String;

    public PropertySpec ToSpec() => new()
    {
        Name = Name.Trim(),
        Type = Type,
        Required = Required,
        MaxLength = Type == PropertyType.String && MaxLength is { } max ? (int)max : null,
    };

    public static PropertyViewModel From(PropertySpec spec, Action changed) =>
        new(changed) { Name = spec.Name, Type = spec.Type, Required = spec.Required, MaxLength = spec.MaxLength };

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        changed();
    }
}

public sealed partial class EntityViewModel : ObservableObject
{
    private readonly Action _changed;

    public EntityViewModel(Action changed)
    {
        _changed = changed;
        Properties.CollectionChanged += (_, _) => changed();
    }

    public static IReadOnlyList<string> Mappings { get; } = ["Default", "Manual", "Mapperly"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PluralPlaceholder), nameof(Display))]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Plural { get; set; }

    [ObservableProperty]
    public partial bool ClientEntity { get; set; }

    [ObservableProperty]
    public partial string Mapping { get; set; } = "Default";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrors))]
    public partial IReadOnlyList<string> Errors { get; set; } = [];

    public bool HasErrors => Errors.Count > 0;

    public ObservableCollection<PropertyViewModel> Properties { get; } = [];

    public string PluralPlaceholder => Name.Length > 0 ? EntitySpec.Pluralize(Name.Trim()) : L.Get("Plural");

    public string Display => Name.Length > 0 ? Name : L.Get("(unnamed)");

    [RelayCommand]
    public void AddProperty() => Properties.Add(new PropertyViewModel(_changed) { Name = Properties.Count == 0 ? "Name" : string.Empty, Required = Properties.Count == 0, MaxLength = Properties.Count == 0 ? 200 : null });

    [RelayCommand]
    public void RemoveProperty(PropertyViewModel property) => Properties.Remove(property);

    public EntitySpec ToSpec() => new()
    {
        Name = Name.Trim(),
        Plural = string.IsNullOrWhiteSpace(Plural) ? null : Plural.Trim(),
        ClientEntity = ClientEntity,
        Mapping = Enum.TryParse<MappingStrategy>(Mapping, out var mapping) ? mapping : null,
        Properties = [.. Properties.Select(p => p.ToSpec())],
    };

    public static EntityViewModel From(EntitySpec spec, Action changed)
    {
        var entity = new EntityViewModel(changed) { Name = spec.Name, Plural = spec.Plural, ClientEntity = spec.ClientEntity, Mapping = spec.Mapping?.ToString() ?? "Default" };
        foreach (var property in spec.Properties)
        {
            entity.Properties.Add(PropertyViewModel.From(property, changed));
        }

        return entity;
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

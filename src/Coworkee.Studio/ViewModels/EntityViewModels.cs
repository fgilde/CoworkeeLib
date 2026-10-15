using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Localization;

namespace Coworkee.Studio.ViewModels;

public sealed partial class PropertyViewModel(Action changed) : ObservableObject
{
    public static IReadOnlyList<PropertyType> Types { get; } = System.Enum.GetValues<PropertyType>();

    public static IReadOnlyList<string> DeleteBehaviors { get; } = ["Default", .. System.Enum.GetNames<DeleteBehavior>()];

    public static IReadOnlyList<string> SearchModes { get; } = ["Default", "Yes", "No"];

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsText), nameof(IsEnum), nameof(IsReference), nameof(IsDecimal), nameof(Summary))]
    public partial PropertyType Type { get; set; }

    [ObservableProperty]
    public partial bool Required { get; set; }

    [ObservableProperty]
    public partial decimal? MaxLength { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial string? Enum { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial string? Reference { get; set; }

    [ObservableProperty]
    public partial string? ReferenceDisplay { get; set; }

    [ObservableProperty]
    public partial string OnDelete { get; set; } = "Default";

    [ObservableProperty]
    public partial string? Inverse { get; set; }

    [ObservableProperty]
    public partial decimal? Precision { get; set; }

    [ObservableProperty]
    public partial decimal? Scale { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial string? Default { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial string? Label { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial bool Unique { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial bool Display { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial bool Facet { get; set; }

    [ObservableProperty]
    public partial string Searchable { get; set; } = "Default";

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public bool IsText => Type is PropertyType.String or PropertyType.Email or PropertyType.Url;

    public bool IsEnum => Type == PropertyType.Enum;

    public bool IsReference => Type == PropertyType.Reference;

    public bool IsDecimal => Type is PropertyType.Decimal or PropertyType.Money;

    /// <summary>The settings beyond name, type and required, in one line under the row.</summary>
    public string Summary => string.Join(" · ", new[]
    {
        IsEnum && Enum is { Length: > 0 } ? Enum : null,
        IsReference && Reference is { Length: > 0 } ? "→ " + Reference : null,
        Label is { Length: > 0 } ? $"\"{Label}\"" : null,
        Default is { Length: > 0 } ? "= " + Default : null,
        Unique ? L.Get("unique") : null,
        Display ? L.Get("display") : null,
        Facet ? L.Get("facet") : null,
    }.OfType<string>());

    public PropertySpec ToSpec() => new()
    {
        Name = Name.Trim(),
        Type = Type,
        Required = Required,
        MaxLength = IsText && MaxLength is { } max ? (int)max : null,
        Precision = IsDecimal && Precision is { } precision ? (int)precision : null,
        Scale = IsDecimal && Scale is { } scale ? (int)scale : null,
        Enum = IsEnum ? Blank(Enum) : null,
        Reference = IsReference ? Blank(Reference) : null,
        ReferenceDisplay = IsReference ? Blank(ReferenceDisplay) : null,
        OnDelete = IsReference && System.Enum.TryParse<DeleteBehavior>(OnDelete, out var behavior) ? behavior : null,
        Inverse = IsReference ? Blank(Inverse) : null,
        Default = Blank(Default),
        Label = Blank(Label),
        Unique = Unique,
        Display = Display,
        Facet = Facet,
        Searchable = Searchable switch { "Yes" => true, "No" => false, _ => null },
    };

    public static PropertyViewModel From(PropertySpec spec, Action changed) => new(changed)
    {
        Name = spec.Name,
        Type = spec.Type,
        Required = spec.Required,
        MaxLength = spec.MaxLength,
        Precision = spec.Precision,
        Scale = spec.Scale,
        Enum = spec.Enum,
        Reference = spec.Reference,
        ReferenceDisplay = spec.ReferenceDisplay,
        OnDelete = spec.OnDelete?.ToString() ?? "Default",
        Inverse = spec.Inverse,
        Default = spec.Default,
        Label = spec.Label,
        Unique = spec.Unique,
        Display = spec.Display,
        Facet = spec.Facet,
        Searchable = spec.Searchable switch { true => "Yes", false => "No", null => "Default" },
    };

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not (nameof(IsExpanded) or nameof(Summary)))
        {
            changed();
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

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

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public bool HasErrors => Errors.Count > 0;

    public ObservableCollection<PropertyViewModel> Properties { get; } = [];

    public string PluralPlaceholder => Name.Length > 0 ? EntitySpec.Pluralize(Name.Trim()) : L.Get("Plural");

    public string Display => Name.Length > 0 ? Name : L.Get("(unnamed)");

    /// <summary>Where the diagram shows the entity; null until it is moved or arranged.</summary>
    public LayoutSpec? Layout { get; private set; }

    public void MoveTo(double x, double y)
    {
        Layout = new LayoutSpec { X = Math.Round(x), Y = Math.Round(y) };
        _changed();
    }

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
        Layout = Layout is null ? null : new LayoutSpec { X = Layout.X, Y = Layout.Y },
    };

    public static EntityViewModel From(EntitySpec spec, Action changed)
    {
        var entity = new EntityViewModel(changed) { Name = spec.Name, Plural = spec.Plural, ClientEntity = spec.ClientEntity, Mapping = spec.Mapping?.ToString() ?? "Default", Layout = spec.Layout };
        foreach (var property in spec.Properties)
        {
            entity.Properties.Add(PropertyViewModel.From(property, changed));
        }

        return entity;
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not (nameof(Errors) or nameof(HasErrors) or nameof(IsSelected)))
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

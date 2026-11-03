using CommunityToolkit.Mvvm.ComponentModel;
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

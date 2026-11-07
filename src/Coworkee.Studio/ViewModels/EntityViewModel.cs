using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Localization;

namespace Coworkee.Studio.ViewModels;

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

    /// <summary>States, transitions and approvals of the entity (Coworkee.Workflows); null without.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWorkflow))]
    public partial WorkflowViewModel? Workflow { get; set; }

    public bool HasWorkflow
    {
        get => Workflow is not null;
        set => Workflow = value ? Workflow ?? NewWorkflow() : null;
    }

    private WorkflowViewModel NewWorkflow()
    {
        var workflow = new WorkflowViewModel(_changed);
        workflow.AddTransition();
        return workflow;
    }

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
        Workflow = Workflow?.ToSpec(),
    };

    public static EntityViewModel From(EntitySpec spec, Action changed)
    {
        var entity = new EntityViewModel(changed) { Name = spec.Name, Plural = spec.Plural, ClientEntity = spec.ClientEntity, Mapping = spec.Mapping?.ToString() ?? "Default", Layout = spec.Layout,
            Workflow = spec.Workflow is null ? null : WorkflowViewModel.From(spec.Workflow, changed) };
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

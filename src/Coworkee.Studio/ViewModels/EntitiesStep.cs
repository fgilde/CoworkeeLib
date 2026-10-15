using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coworkee.Cli.Spec;
using Coworkee.Cli.Spec.Import;
using Coworkee.Studio.Localization;
using Coworkee.Studio.Model;

namespace Coworkee.Studio.ViewModels;

public sealed partial class EntitiesStep : StepViewModel
{
    public EntitiesStep(MainViewModel main)
        : base(main) => Import = new EntityImportViewModel(this);

    protected override string TitleText => "Entities";

    protected override string SubtitleText => "Feature modules with their entities; the CLI generates table, API, permissions and a page for each.";

    protected override string PitchText => "Describe your domain now and start with working pages: every entity gets a table, an OData set, endpoints, validation, permissions and a Blazor page with create, edit and delete.";

    public override string Icon => "Icon.Table";

    public ObservableCollection<ModuleViewModel> Modules { get; } = [];

    public ObservableCollection<EnumViewModel> Enums { get; } = [];

    /// <summary>What a reference can point to: Entity within a module, Module.Entity across modules.</summary>
    public IReadOnlyList<string> ReferenceTargets => [.. Modules.SelectMany(m => m.Entities.Where(e => e.Name.Length > 0).Select(e => $"{m.Name}.{e.Name}"))];

    public IReadOnlyList<string> EnumNames => [.. Enums.Where(e => e.Name.Length > 0).Select(e => e.Name)];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasModule))]
    public partial ModuleViewModel? SelectedModule { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEntity), nameof(Preview))]
    public partial EntityViewModel? SelectedEntity { get; set; }

    public bool HasModules => Modules.Count > 0;

    public EntityImportViewModel Import { get; }

    [ObservableProperty]
    public partial bool IsImportOpen { get; set; }

    public static IReadOnlyList<string> ViewModes { get; } = ["List", "Diagram"];

    /// <summary>List: modules and the entity editor; Diagram: boxes and lines with the editor beside them.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDiagram), nameof(IsList))]
    public partial string ViewMode { get; set; } = "List";

    public bool IsDiagram => ViewMode == "Diagram";

    public bool IsList => !IsDiagram;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingReference))]
    public partial ReferenceDraft? PendingReference { get; set; }

    public bool HasPendingReference => PendingReference is not null;

    /// <summary>Problems of the spec as a whole: enums without modules, modules referencing each other.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> SpecErrors { get; set; } = [];

    public bool HasModule => SelectedModule is not null;

    public bool HasEntity => SelectedEntity is not null;

    public string DefaultMapping => L.Format("Default ({0})", Main.Option("mapping").Value ?? "manual");

    public EntityPreview? Preview => SelectedModule is { } module && SelectedEntity is { } entity && entity.Name.Length > 0
        ? EntityPreview.For(Main.Basics.Name is { Length: > 0 } app ? app : "MyApp", module.Name.Length > 0 ? module.Name : "Module", entity.ToSpec())
        : null;

    public string Counts => L.Format("{0} modules · {1} entities", Modules.Count, Modules.Sum(m => m.Entities.Count));

    public override bool Owns(string error) =>
        error.StartsWith("modules", StringComparison.Ordinal) || error.StartsWith("enums", StringComparison.Ordinal) || error.StartsWith("options.clientEntities", StringComparison.Ordinal);

    [RelayCommand]
    public void AddEnum()
    {
        Enums.Add(new EnumViewModel(Changed) { Name = Unique("Status", Enums.Select(e => e.Name)), Values = "Open, Done" });
        Changed();
    }

    [RelayCommand]
    public void RemoveEnum(EnumViewModel item)
    {
        Enums.Remove(item);
        Changed();
    }

    [RelayCommand]
    public void AddModule()
    {
        var module = new ModuleViewModel(Changed) { Name = Unique("Sales", Modules.Select(m => m.Name)) };
        Modules.Add(module);
        SelectedModule = module;
        AddEntity();
    }

    [RelayCommand]
    public void RemoveModule(ModuleViewModel module)
    {
        Modules.Remove(module);
        SelectedModule = Modules.FirstOrDefault();
        SelectedEntity = SelectedModule?.Entities.FirstOrDefault();
        Changed();
    }

    [RelayCommand]
    public void AddEntity()
    {
        if (SelectedModule is not { } module)
        {
            AddModule();
            return;
        }

        var entity = new EntityViewModel(Changed) { Name = Unique("Item", Modules.SelectMany(m => m.Entities).Select(e => e.Name)) };
        entity.AddProperty();
        module.Entities.Add(entity);
        SelectedEntity = entity;
    }

    [RelayCommand]
    public void AddEntityTo(ModuleViewModel module)
    {
        SelectedModule = module;
        AddEntity();
    }

    [RelayCommand]
    public void RemoveEntity(EntityViewModel entity)
    {
        var module = Modules.First(m => m.Entities.Contains(entity));
        module.Entities.Remove(entity);
        SelectedEntity = module.Entities.FirstOrDefault();
    }

    [RelayCommand]
    public void SelectEntity(EntityViewModel entity)
    {
        SelectedModule = Modules.FirstOrDefault(m => m.Entities.Contains(entity));
        SelectedEntity = entity;
    }

    [RelayCommand]
    public void AddExample()
    {
        var example = SpecJson.Example();
        foreach (var module in example.Modules.Where(m => Modules.All(x => !x.Name.Equals(m.Name, StringComparison.OrdinalIgnoreCase))))
        {
            Modules.Add(ModuleViewModel.From(module, Changed));
        }

        foreach (var item in example.Enums.Where(e => Enums.All(x => !x.Name.Equals(e.Name, StringComparison.OrdinalIgnoreCase))))
        {
            Enums.Add(EnumViewModel.From(item, Changed));
        }

        SelectedModule = Modules.LastOrDefault();
        SelectedEntity = SelectedModule?.Entities.FirstOrDefault();
        Changed();
    }

    [RelayCommand]
    public void OpenImport() => IsImportOpen = true;

    [RelayCommand]
    public void AutoLayout()
    {
        foreach (var (entity, point) in DiagramLayout.Arrange(this))
        {
            entity.MoveTo(point.X, point.Y);
        }
    }

    /// <summary>A line drawn from <paramref name="source"/> to <paramref name="target"/> in the diagram; the dialog asks for the rest.</summary>
    public void BeginReference(EntityViewModel source, EntityViewModel target)
    {
        var taken = source.Properties.Select(p => p.Name).Concat(source.Properties.Where(p => p.IsReference).Select(p => p.Name + "Id"));
        PendingReference = new ReferenceDraft(source, target, Unique(target.Name, taken));
    }

    [RelayCommand]
    public void ConfirmReference()
    {
        if (PendingReference is not { } draft)
        {
            return;
        }

        var module = Modules.First(m => m.Entities.Contains(draft.Source));
        var target = Modules.First(m => m.Entities.Contains(draft.Target));
        draft.Source.Properties.Add(new PropertyViewModel(Changed)
        {
            Name = draft.Name.Trim(),
            Type = PropertyType.Reference,
            Reference = module == target ? draft.Target.Name : $"{target.Name}.{draft.Target.Name}",
            Required = draft.Required,
            OnDelete = draft.OnDelete,
        });
        PendingReference = null;
        SelectEntity(draft.Source);
    }

    [RelayCommand]
    public void CancelReference() => PendingReference = null;

    /// <summary>Imported entities join a module of the same name or come as a new one; enums only when the name is free.</summary>
    public void AddImported(ImportResult result)
    {
        var module = Modules.FirstOrDefault(m => m.Name.Equals(result.Module.Name, StringComparison.OrdinalIgnoreCase));
        if (module is null)
        {
            Modules.Add(module = ModuleViewModel.From(result.Module, Changed));
        }
        else
        {
            foreach (var entity in result.Module.Entities)
            {
                module.Entities.Add(EntityViewModel.From(entity, Changed));
            }
        }

        foreach (var item in result.Enums.Where(e => Enums.All(x => !x.Name.Equals(e.Name, StringComparison.OrdinalIgnoreCase))))
        {
            Enums.Add(EnumViewModel.From(item, Changed));
        }

        SelectedModule = module;
        SelectedEntity = module.Entities.LastOrDefault();
        Changed();
    }

    public List<ModuleSpec> ToSpec() => [.. Modules.Select(m => m.ToSpec())];

    public List<EnumSpec> EnumSpecs() => [.. Enums.Select(e => e.ToSpec())];

    public void Load(IEnumerable<ModuleSpec> modules, IEnumerable<EnumSpec> enums)
    {
        Modules.Clear();
        foreach (var module in modules)
        {
            Modules.Add(ModuleViewModel.From(module, Changed));
        }

        Enums.Clear();
        foreach (var item in enums)
        {
            Enums.Add(EnumViewModel.From(item, Changed));
        }

        SelectedModule = Modules.FirstOrDefault();
        SelectedEntity = SelectedModule?.Entities.FirstOrDefault();
    }

    public override void Refresh()
    {
        for (var i = 0; i < Modules.Count; i++)
        {
            var prefix = $"modules[{i}]";
            var module = Modules[i];
            module.Errors = [.. Errors.Where(e => e.StartsWith(prefix + ".name", StringComparison.Ordinal) || e.StartsWith(prefix + ".entities:", StringComparison.Ordinal)).Select(Message)];
            for (var j = 0; j < module.Entities.Count; j++)
            {
                var entityPrefix = $"{prefix}.entities[{j}]";
                module.Entities[j].Errors = [.. Errors.Where(e => e.StartsWith(entityPrefix + ".", StringComparison.Ordinal)).Select(e => Describe(e[(entityPrefix.Length + 1)..], module.Entities[j]))];
            }
        }

        for (var i = 0; i < Enums.Count; i++)
        {
            var prefix = $"enums[{i}]";
            Enums[i].Errors = [.. Errors.Where(e => e.StartsWith(prefix, StringComparison.Ordinal)).Select(Message)];
        }

        SpecErrors = [.. Errors.Where(e => e.StartsWith("enums:", StringComparison.Ordinal) || e.StartsWith("modules:", StringComparison.Ordinal)).Select(Message)];
        OnPropertyChanged(nameof(ReferenceTargets));
        OnPropertyChanged(nameof(EnumNames));
        OnPropertyChanged(nameof(HasModules));
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(Counts));
        OnPropertyChanged(nameof(DefaultMapping));
    }

    partial void OnSelectedEntityChanged(EntityViewModel? value)
    {
        foreach (var entity in Modules.SelectMany(m => m.Entities))
        {
            entity.IsSelected = entity == value;
        }
    }

    partial void OnSelectedModuleChanged(ModuleViewModel? value)
    {
        if (value is not null && (SelectedEntity is null || !value.Entities.Contains(SelectedEntity)))
        {
            SelectedEntity = value.Entities.FirstOrDefault();
        }
    }

    private void Changed() => Main.SpecChanged();

    private static string Message(string error) => error[(error.IndexOf(": ", StringComparison.Ordinal) + 2)..];

    /// <summary>properties[1].name: ... becomes Property 2 (Email): ...</summary>
    private static string Describe(string error, EntityViewModel entity)
    {
        if (error.StartsWith("properties[", StringComparison.Ordinal) && int.TryParse(error[11..error.IndexOf(']')], out var index))
        {
            var name = index < entity.Properties.Count && entity.Properties[index].Name.Length > 0 ? $" ({entity.Properties[index].Name})" : string.Empty;
            return L.Format("Property {0}{1}: {2}", index + 1, name, Message(error));
        }

        return Message(error);
    }

    private static string Unique(string name, IEnumerable<string> taken)
    {
        var used = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Enumerable.Range(1, 99).Select(i => i == 1 ? name : name + i).First(n => !used.Contains(n));
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Localization;
using Coworkee.Studio.Model;

namespace Coworkee.Studio.ViewModels;

public sealed partial class EntitiesStep(MainViewModel main) : StepViewModel(main)
{
    protected override string TitleText => "Entities";

    protected override string SubtitleText => "Feature modules with their entities; the CLI generates table, API, permissions and a page for each.";

    protected override string PitchText => "Describe your domain now and start with working pages: every entity gets a table, an OData set, endpoints, validation, permissions and a Blazor page with create, edit and delete.";

    public override string Icon => "Icon.Table";

    public ObservableCollection<ModuleViewModel> Modules { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasModule))]
    public partial ModuleViewModel? SelectedModule { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEntity), nameof(Preview))]
    public partial EntityViewModel? SelectedEntity { get; set; }

    public bool HasModules => Modules.Count > 0;

    public bool HasModule => SelectedModule is not null;

    public bool HasEntity => SelectedEntity is not null;

    public string DefaultMapping => L.Format("Default ({0})", Main.Option("mapping").Value ?? "manual");

    public EntityPreview? Preview => SelectedModule is { } module && SelectedEntity is { } entity && entity.Name.Length > 0
        ? EntityPreview.For(Main.Basics.Name is { Length: > 0 } app ? app : "MyApp", module.Name.Length > 0 ? module.Name : "Module", entity.ToSpec())
        : null;

    public string Counts => L.Format("{0} modules · {1} entities", Modules.Count, Modules.Sum(m => m.Entities.Count));

    public override bool Owns(string error) => error.StartsWith("modules", StringComparison.Ordinal) || error.StartsWith("options.clientEntities", StringComparison.Ordinal);

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
        foreach (var module in SpecJson.Example().Modules.Where(m => Modules.All(x => !x.Name.Equals(m.Name, StringComparison.OrdinalIgnoreCase))))
        {
            Modules.Add(ModuleViewModel.From(module, Changed));
        }

        SelectedModule = Modules.LastOrDefault();
        SelectedEntity = SelectedModule?.Entities.FirstOrDefault();
        Changed();
    }

    public List<ModuleSpec> ToSpec() => [.. Modules.Select(m => m.ToSpec())];

    public void Load(IEnumerable<ModuleSpec> modules)
    {
        Modules.Clear();
        foreach (var module in modules)
        {
            Modules.Add(ModuleViewModel.From(module, Changed));
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

        OnPropertyChanged(nameof(HasModules));
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(Counts));
        OnPropertyChanged(nameof(DefaultMapping));
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

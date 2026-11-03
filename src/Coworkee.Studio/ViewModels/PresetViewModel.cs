using CommunityToolkit.Mvvm.ComponentModel;
using Coworkee.Studio.Localization;
using Coworkee.Studio.Model;

namespace Coworkee.Studio.ViewModels;

public sealed partial class PresetViewModel(Preset preset) : ObservableObject
{
    public Preset Preset => preset;

    public string Name => L.Get(preset.Name);

    public string Description => L.Get(preset.Description);

    public string Icon => preset.Icon;

    [ObservableProperty]
    public partial bool IsActive { get; set; }
}

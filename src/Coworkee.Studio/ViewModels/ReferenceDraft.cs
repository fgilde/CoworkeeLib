using CommunityToolkit.Mvvm.ComponentModel;
using Coworkee.Studio.Localization;

namespace Coworkee.Studio.ViewModels;

/// <summary>A reference drawn in the diagram, waiting for its name, required flag and delete behaviour.</summary>
public sealed partial class ReferenceDraft(EntityViewModel source, EntityViewModel target, string name) : ObservableObject
{
    public EntityViewModel Source => source;

    public EntityViewModel Target => target;

    public string Title => L.Format("{0} references {1}", source.Display, target.Display);

    [ObservableProperty]
    public partial string Name { get; set; } = name;

    [ObservableProperty]
    public partial bool Required { get; set; }

    [ObservableProperty]
    public partial string OnDelete { get; set; } = "Default";
}

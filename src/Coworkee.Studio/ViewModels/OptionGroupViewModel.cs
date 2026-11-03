using Coworkee.Studio.Localization;

namespace Coworkee.Studio.ViewModels;

public sealed record OptionGroupViewModel(string Name, string Description, string Icon, IReadOnlyList<OptionViewModel> Options)
{
    public string Title => L.Get(Name);

    public string Text => L.Get(Description);
}

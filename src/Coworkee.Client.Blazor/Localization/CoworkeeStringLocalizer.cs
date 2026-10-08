using Microsoft.Extensions.Localization;

namespace Coworkee.Client.Blazor.Localization;

/// <summary>Lets MudBlazor.Extensions (object edit labels, dialogs) translate through the app's texts.</summary>
internal sealed class CoworkeeStringLocalizer<T>(CoworkeeLocalizer localizer) : IStringLocalizer<T>
{
    public LocalizedString this[string name] => new(name, localizer[name]);

    public LocalizedString this[string name, params object[] arguments] => new(name, localizer[name, arguments]);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}

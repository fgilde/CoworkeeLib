using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Coworkee.Client.Blazor.Localization;

/// <summary>Object edit labels without an explicit label: the [Display] name, else "ShowLogoInNav" becomes "Show logo in nav".</summary>
public static class PropertyLabels
{
    public static string For(PropertyInfo property) =>
        property.GetCustomAttribute<DisplayAttribute>()?.GetName() is { Length: > 0 } name ? name : Humanize(property.Name);

    public static string Humanize(string name) => Contracts.Data.EnumLabels.Humanize(name);
}

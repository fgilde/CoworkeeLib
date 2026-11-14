using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Settings;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components;

/// <summary>
/// One setting of a <see cref="SettingsModel"/> as a control of its type, for pages that arrange settings by hand: a switch, a number with its unit, a select or a text.
/// Renders nothing when the app defines no such setting for the scope; a value the host fixes shows read-only with the hint why.
/// </summary>
public partial class SettingField
{
    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public SettingsModel Model { get; set; } = null!;

    [Parameter, EditorRequired] public string Name { get; set; } = string.Empty;

    /// <summary>Shown after a number, e.g. "min" or "characters".</summary>
    [Parameter] public string? Unit { get; set; }

    [Parameter] public int Min { get; set; }

    [Parameter] public int Max { get; set; } = int.MaxValue;

    /// <summary>Names of the choices of a choice setting; the others show translated or as they are.</summary>
    [Parameter] public IReadOnlyDictionary<string, string>? ChoiceLabels { get; set; }

    /// <summary>After every change, so the page updates what depends on it (its save bar).</summary>
    [Parameter] public EventCallback Changed { get; set; }

    private SettingDefinitionDto? Setting => Model.Definition(Name);

    // a fixed value applies whatever is stored, so the control shows it
    private object? Shown => Setting is { FixedValue: { } fixedValue, Type: SettingType.Bool } ? fixedValue == "true"
        : Setting is { FixedValue: { } text } ? text
        : Model.Get(Name);

    private string? Hint
    {
        get
        {
            var description = Setting?.Description is { Length: > 0 } text ? L[text] : null;
            if (Setting?.FixedValue is not { } fixedValue)
            {
                return description;
            }

            var hint = L["The host fixes {0} in its configuration; the stored value applies once it no longer does.", L.Find(fixedValue) ?? fixedValue];
            return description is null ? hint : $"{description} {hint}";
        }
    }

    private string ChoiceLabel(string? choice) => choice is null ? string.Empty : ChoiceLabels?.GetValueOrDefault(choice) ?? L.Find(choice) ?? choice;

    private Task SetAsync(object? value)
    {
        Model.Set(Name, value);
        return Changed.InvokeAsync();
    }
}

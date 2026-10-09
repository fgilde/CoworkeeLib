using Microsoft.AspNetCore.Components;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;

namespace Coworkee.Client.Blazor.Components;

/// <summary>
/// Replaces MudEx's object edit dialog (AddCoworkeeClient): a list item of a settings section edits without the global reset,
/// like the settings form. MudExCollectionEditor offers no way to pass that to its item dialog.
/// </summary>
public class SettingsItemDialog<T> : MudExObjectEditDialog<T>
{
    private static readonly GlobalResetSettings NoReset = new() { AllowReset = false };

    [Inject] private IEnumerable<ClientAppConfiguration> Sections { get; set; } = null!;

    public override Task SetParametersAsync(ParameterView parameters)
    {
        if (!parameters.TryGetValue<GlobalResetSettings>(nameof(GlobalResetSettings), out _) && SettingsItemMeta<T>.Applies(Sections))
        {
            GlobalResetSettings = NoReset;
        }

        return base.SetParametersAsync(parameters);
    }
}

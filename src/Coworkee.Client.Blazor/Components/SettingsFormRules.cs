using MudBlazor;
using MudBlazor.Extensions;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;
using MudBlazor.Extensions.Options;

namespace Coworkee.Client.Blazor.Components;

/// <summary>
/// How a settings form and the objects edited from it (list items in their dialog) look: two columns, lists over the full width
/// without their own search, no computed values, no reset per field.
/// </summary>
public static class SettingsFormRules
{
    private static readonly PropertyResetSettings NoReset = new() { AllowReset = false };

#pragma warning disable BL0005 // MudEx configures the wrapping grid items through these instances
    public static void Apply<T>(ObjectEditMeta<T> meta)
    {
        meta.WrapEachInMudItem(i =>
        {
            i.xs = 12;
            i.md = 6;
        });
        foreach (var property in meta.AllProperties)
        {
            property.WithResetOptions(NoReset);
            if (IsList(property))
            {
                property.WrapInMudItem(i => i.xs = 12);
                property.WithAdditionalAttributes(true,
                    new KeyValuePair<string, object>(nameof(MudExCollectionEditor<string>.FilterMode), PropertyFilterMode.Disabled),
                    new KeyValuePair<string, object>(nameof(MudExCollectionEditor<string>.DialogOptions), ItemDialog()));
            }
            else if (!property.PropertyInfo.CanWrite && property.Children?.Any() != true)
            {
                property.Ignore(); // computed, like DisplayName
            }
        }
    }
#pragma warning restore BL0005

    private static bool IsList(ObjectEditPropertyMeta property) =>
        property.PropertyInfo.PropertyType != typeof(string) && property.PropertyInfo.PropertyType.IsAssignableTo(typeof(System.Collections.IEnumerable));

    private static DialogOptionsEx ItemDialog() => new()
    {
        MaxWidth = MaxWidth.Medium,
        FullWidth = true,
        CloseButton = true,
        Resizeable = true,
        DragMode = MudDialogDragMode.Simple,
    };
}

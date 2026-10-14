using System.ComponentModel.DataAnnotations;
using System.Reflection;
using MudBlazor;
using MudBlazor.Extensions;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;
using MudBlazor.Extensions.Core;
using MudBlazor.Extensions.Options;

namespace Coworkee.Client.Blazor.Components;

/// <summary>
/// How a settings form and the objects edited from it (list items in their dialog) look: two columns, lists over the full width
/// without their own search, no computed values, no reset per field; [Display] gives the help text (Description) and the group (GroupName).
/// </summary>
public static class SettingsFormRules
{
    private static readonly PropertyResetSettings NoReset = new() { AllowReset = false };

    /// <summary>The setup every settings form shares (as <c>@attributes</c> of the MudExObjectEditForm): sticky toolbar with search, export and import, several groups open, no reset per field (restoring goes through the toolbar or cancel).</summary>
    public static readonly IReadOnlyDictionary<string, object> FormAttributes = new Dictionary<string, object>
    {
        [nameof(MudExObjectEditForm<object>.SingleExpand)] = false,
        [nameof(MudExObjectEditForm<object>.StickyToolbar)] = true,
        [nameof(MudExObjectEditForm<object>.StickyActionBar)] = true,
        [nameof(MudExObjectEditForm<object>.AllowExport)] = true,
        [nameof(MudExObjectEditForm<object>.AllowImport)] = true,
        [nameof(MudExObjectEditForm<object>.ImportNeedsConfirmation)] = true,
        [nameof(MudExObjectEditForm<object>.ToolbarColor)] = new MudExColor(Color.Surface),
        [nameof(MudExObjectEditForm<object>.ActionBarColor)] = new MudExColor(Color.Surface),
        [nameof(MudExObjectEditForm<object>.ActionBarClass)] = "border-t border-solid mud-border-lines-default",
        [nameof(MudExObjectEditForm<object>.ToolBarActionAlignment)] = ActionAlignment.Left,
        [nameof(MudExObjectEditForm<object>.GroupingStyle)] = GroupingStyle.Flat,
        [nameof(MudExObjectEditForm<object>.GroupLineColor)] = new MudExColor("--mud-palette-lines-default"),
        [nameof(MudExObjectEditForm<object>.PathDisplayMode)] = PathDisplayMode.None,
        [nameof(MudExObjectEditForm<object>.GlobalResetSettings)] = new GlobalResetSettings { AllowReset = false },
        [nameof(MudExObjectEditForm<object>.DefaultPropertyResetSettings)] = NoReset,
    };

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
            if (property.PropertyInfo.GetCustomAttribute<DisplayAttribute>() is { } display)
            {
                Describe(property, display);
            }

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

    private static void Describe(ObjectEditPropertyMeta property, DisplayAttribute display)
    {
        if (display.GetDescription() is { Length: > 0 } description)
        {
            property.WithDescription(description);
        }

        if (display.GetGroupName() is { Length: > 0 } group)
        {
            property.WithGroup(group);
        }
    }

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

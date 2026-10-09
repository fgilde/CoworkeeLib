using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Features;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

/// <summary>Feature values of an edition or the overrides of a tenant; an empty field inherits.</summary>
public partial class FeatureValuesDialog
{
    private Dictionary<string, string> _values = [];

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;

    [Parameter] public string Title { get; set; } = string.Empty;

    [Parameter] public IReadOnlyList<FeatureGroupDto> Groups { get; set; } = [];

    [Parameter] public IReadOnlyDictionary<string, string> Values { get; set; } = new Dictionary<string, string>();

    /// <summary>What applies without an own value: the defaults, for a tenant merged with its edition.</summary>
    [Parameter] public IReadOnlyDictionary<string, string?> Inherited { get; set; } = new Dictionary<string, string?>();

    protected override void OnParametersSet() => _values = new Dictionary<string, string>(Values, StringComparer.Ordinal);

    private string? Value(FeatureDefinitionDto feature) => _values.GetValueOrDefault(feature.Name);

    private string Inherits(FeatureDefinitionDto feature) => L["Inherited: {0}", Inherited.GetValueOrDefault(feature.Name) ?? "–"];

    private void Set(FeatureDefinitionDto feature, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _values.Remove(feature.Name);
        }
        else
        {
            _values[feature.Name] = value.Trim();
        }
    }

    /// <summary>The edited values, or null when cancelled.</summary>
    public static async Task<Dictionary<string, string>?> ShowAsync(
        IDialogService dialogs, string title, IReadOnlyList<FeatureGroupDto> groups, IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, string?> inherited)
    {
        var reference = await dialogs.ShowSideSheetAsync<FeatureValuesDialog>(title, new DialogParameters<FeatureValuesDialog>
        {
            { d => d.Title, title },
            { d => d.Groups, groups },
            { d => d.Values, values },
            { d => d.Inherited, inherited },
        });
        var result = await reference.Result;
        return result is { Canceled: false, Data: Dictionary<string, string> edited } ? edited : null;
    }

    /// <summary>The defaults of all features, with <paramref name="over"/> on top.</summary>
    public static IReadOnlyDictionary<string, string?> Defaults(IReadOnlyList<FeatureGroupDto> groups, IReadOnlyDictionary<string, string>? over = null) =>
        groups.SelectMany(g => g.Features).ToDictionary(f => f.Name, f => over?.GetValueOrDefault(f.Name) ?? f.DefaultValue, StringComparer.Ordinal);
}

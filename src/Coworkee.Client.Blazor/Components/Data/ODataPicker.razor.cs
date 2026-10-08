using System.Text.Json;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data;

public sealed record ODataPickerItem(Guid Id, string Text);

/// <summary>Chooses a row of an OData entity set by its id, searching on a text property; for foreign keys in forms.</summary>
public partial class ODataPicker
{
    private ODataPickerItem? _selected;

    [Inject] private IODataClient OData { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public string EntitySet { get; set; } = string.Empty;

    [Parameter] public string TextProperty { get; set; } = "Name";

    [Parameter] public Guid Value { get; set; }

    [Parameter] public EventCallback<Guid> ValueChanged { get; set; }

    [Parameter] public string? Label { get; set; }

    [Parameter] public bool Required { get; set; }

    [Parameter] public bool Disabled { get; set; }

    [Parameter] public bool ReadOnly { get; set; }

    [Parameter] public Variant Variant { get; set; } = Variant.Text;

    [Parameter] public Margin Margin { get; set; } = Margin.None;

    protected override async Task OnParametersSetAsync()
    {
        if (Value == Guid.Empty)
        {
            _selected = null;
        }
        else if (_selected?.Id != Value)
        {
            _selected = (await QueryAsync($"Id eq {Value}", 1, CancellationToken.None)).FirstOrDefault() ?? new ODataPickerItem(Value, Value.ToString());
        }
    }

    private Task<IEnumerable<ODataPickerItem>> SearchAsync(string? text, CancellationToken cancellationToken) =>
        QueryAsync(string.IsNullOrWhiteSpace(text) ? null : ODataFilter.Search(text, [TextProperty]), 50, cancellationToken);

    private async Task<IEnumerable<ODataPickerItem>> QueryAsync(string? filter, int top, CancellationToken cancellationToken)
    {
        var page = await OData.QueryAsync<JsonElement>(EntitySet, new ODataQuery { Filter = filter, OrderBy = TextProperty, Top = top, Count = false }, cancellationToken);
        return page.Items.Select(row => new ODataPickerItem(
            row.GetProperty("Id").GetGuid(),
            row.TryGetProperty(TextProperty, out var text) ? text.ToString() : string.Empty));
    }

    private async Task SelectAsync(ODataPickerItem? item)
    {
        _selected = item;
        await ValueChanged.InvokeAsync(item?.Id ?? Guid.Empty);
    }
}

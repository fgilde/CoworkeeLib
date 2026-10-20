using System.Text.Json;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data;

public sealed record ODataPickerItem(Guid Id, string Text);

/// <summary>
/// Chooses a row of an OData entity set by its id, searching on its text properties; for foreign keys in forms.
/// <see cref="TextProperty"/> takes one property or several separated by commas (<c>"FirstName,LastName"</c>): the text joins them with spaces,
/// and every word of the search must appear in one of them.
/// </summary>
public partial class ODataPicker
{
    private ODataPickerItem? _selected;

    [Inject] private IODataClient OData { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public string EntitySet { get; set; } = string.Empty;

    [Parameter] public string TextProperty { get; set; } = "Name";

    private string[] TextProperties => TextProperty.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    [Parameter] public Guid Value { get; set; }

    [Parameter] public EventCallback<Guid> ValueChanged { get; set; }

    [Parameter] public string? Label { get; set; }

    [Parameter] public bool Required { get; set; }

    [Parameter] public bool Disabled { get; set; }

    [Parameter] public bool ReadOnly { get; set; }

    [Parameter] public Variant Variant { get; set; } = Variant.Text;

    [Parameter] public Margin? Margin { get; set; }

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
        QueryAsync(ODataFilter.And([.. (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word => ODataFilter.Search(word, TextProperties))]), 50, cancellationToken);

    private async Task<IEnumerable<ODataPickerItem>> QueryAsync(string? filter, int top, CancellationToken cancellationToken)
    {
        var properties = TextProperties;
        var query = new ODataQuery { Filter = filter, Select = string.Join(',', ["Id", .. properties]), OrderBy = string.Join(',', properties), Top = top, Count = false };
        var page = await OData.QueryAsync<JsonElement>(EntitySet, query, cancellationToken);
        return page.Items.Select(row => new ODataPickerItem(
            row.GetProperty("Id").GetGuid(),
            string.Join(' ', properties.Select(p => row.TryGetProperty(p, out var text) && text.ValueKind != JsonValueKind.Null ? text.ToString() : null).Where(t => !string.IsNullOrEmpty(t)))));
    }

    private async Task SelectAsync(ODataPickerItem? item)
    {
        _selected = item;
        await ValueChanged.InvokeAsync(item?.Id ?? Guid.Empty);
    }
}

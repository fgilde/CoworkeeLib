using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Editors;

/// <summary>MIME types as chips with readable names; every known type to pick, grouped by kind and searchable. Empty means every type.</summary>
public partial class ContentTypesEditor
{
    private static readonly ICollection<string> Values = [.. ContentTypeCatalog.Known.Select(t => t.Value)];

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public IReadOnlyList<string>? Value { get; set; }

    [Parameter] public EventCallback<IReadOnlyList<string>> ValueChanged { get; set; }

    [Parameter] public string? Label { get; set; }

    [Parameter] public string? HelperText { get; set; }

    [Parameter] public bool ReadOnly { get; set; }

    private IReadOnlyList<string> Selected => Value ?? [];

    private string Name(string value) => L[ContentTypeCatalog.Describe(value).Name];

    private static string Detail(ContentTypeInfo type) => type.Name == type.Value ? type.Extensions : $"{type.Value}  {type.Extensions}";

    private bool Matches(string value, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var type = ContentTypeCatalog.Describe(value);
        return new[] { type.Value, type.Extensions, Name(value) }.Any(s => s.Contains(text.Trim(), StringComparison.CurrentCultureIgnoreCase));
    }

    // keeps the order and the types the catalog does not know (set in the appsettings), adds new picks at the end
    private Task SelectAsync(IEnumerable<string>? picked)
    {
        var values = picked?.ToList() ?? [];
        return SetAsync([.. Selected.Where(v => values.Contains(v) || !Values.Contains(v)), .. values.Where(v => !Selected.Contains(v))]);
    }

    private Task AddAsync(IReadOnlyList<string> values) =>
        SetAsync([.. Selected, .. values.Where(v => !Selected.Contains(v, StringComparer.OrdinalIgnoreCase))]);

    private Task RemoveAsync(string value) => SetAsync([.. Selected.Where(v => v != value)]);

    private async Task SetAsync(IReadOnlyList<string> values)
    {
        Value = values;
        await ValueChanged.InvokeAsync(values);
    }
}

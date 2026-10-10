using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Editors;

/// <summary>MIME types as chips with readable names; every known type can be found by name, extension or MIME type, a kind adds a whole group. Empty means every type.</summary>
public partial class ContentTypesEditor
{
    // ponytail: a search instead of a list of every known type - 768 options rendered at once block WebAssembly for seconds
    private const int MaxResults = 50;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public IReadOnlyList<string>? Value { get; set; }

    [Parameter] public EventCallback<IReadOnlyList<string>> ValueChanged { get; set; }

    [Parameter] public string? Label { get; set; }

    [Parameter] public string? HelperText { get; set; }

    [Parameter] public bool ReadOnly { get; set; }

    private IReadOnlyList<string> Selected => Value ?? [];

    private string Name(string? value) => value is null ? string.Empty : L[ContentTypeCatalog.Describe(value).Name];

    private static string Detail(ContentTypeInfo type) => type.Name == type.Value ? type.Extensions : $"{type.Value}  {type.Extensions}";

    private Task<IEnumerable<string>> SearchAsync(string? text, CancellationToken _) =>
        Task.FromResult(ContentTypeCatalog.Known.Select(t => t.Value).Where(v => !Selected.Contains(v) && Matches(v, text)).Take(MaxResults));

    private bool Matches(string value, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var type = ContentTypeCatalog.Describe(value);
        return new[] { type.Value, type.Extensions, Name(value) }.Any(s => s.Contains(text.Trim(), StringComparison.CurrentCultureIgnoreCase));
    }

    private Task PickAsync(string? value) => value is null ? Task.CompletedTask : AddAsync([value]);

    private Task AddAsync(IReadOnlyList<string> values) =>
        SetAsync([.. Selected, .. values.Where(v => !Selected.Contains(v, StringComparer.OrdinalIgnoreCase))]);

    private Task RemoveAsync(string value) => SetAsync([.. Selected.Where(v => v != value)]);

    private async Task SetAsync(IReadOnlyList<string> values)
    {
        Value = values;
        await ValueChanged.InvokeAsync(values);
    }
}

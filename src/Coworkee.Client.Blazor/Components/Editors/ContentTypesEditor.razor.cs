using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Coworkee.Client.Blazor.Components.Editors;

/// <summary>MIME types as chips with readable names; common types and groups to pick, others typed in. Empty means every type.</summary>
public partial class ContentTypesEditor
{
    private string? _input;
    private string? _error;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public IReadOnlyList<string>? Value { get; set; }

    [Parameter] public EventCallback<IReadOnlyList<string>> ValueChanged { get; set; }

    [Parameter] public string? Label { get; set; }

    [Parameter] public string? HelperText { get; set; }

    [Parameter] public bool ReadOnly { get; set; }

    private IReadOnlyList<string> Selected => Value ?? [];

    private Task<IEnumerable<string>> SearchAsync(string? text, CancellationToken cancellationToken)
    {
        var open = ContentTypeCatalog.Known.Where(t => !Selected.Contains(t.Value, StringComparer.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(text))
        {
            open = open.Where(t => t.Value.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase) || L[t.Name].Contains(text.Trim(), StringComparison.CurrentCultureIgnoreCase));
        }

        return Task.FromResult(open.Select(t => t.Value));
    }

    // a type picked from the list goes in right away; typed ones with enter or the plus
    private Task SelectAsync(string? value)
    {
        _input = value;
        _error = null;
        return ContentTypeCatalog.Known.Any(t => t.Value == value) ? AddAsync([value!]) : Task.CompletedTask;
    }

    private async Task KeyDownAsync(KeyboardEventArgs args)
    {
        if (args.Key == "Enter")
        {
            await AddInputAsync();
        }
    }

    private async Task AddInputAsync()
    {
        var value = _input?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        if (!ContentTypeCatalog.IsValid(value))
        {
            _error = L["Not a file type like image/png or image/*"];
            return;
        }

        await AddAsync([value]);
    }

    private Task AddAsync(IReadOnlyList<string> values)
    {
        _input = null;
        _error = null;
        return SetAsync([.. Selected, .. values.Where(v => !Selected.Contains(v, StringComparer.OrdinalIgnoreCase))]);
    }

    private Task RemoveAsync(string value) => SetAsync([.. Selected.Where(v => v != value)]);

    private async Task SetAsync(IReadOnlyList<string> values)
    {
        Value = values;
        await ValueChanged.InvokeAsync(values);
    }
}

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Coworkee.Client.Blazor.Components.Query;

/// <summary>
/// Builds a filter visually: nested AND/OR groups of rules with an operator and a value editor per field type, a preview as text,
/// OData and JSON, and JSON import and download. Knows nothing about where data lives: translate <see cref="Value"/> with
/// <see cref="ODataQueryTranslator"/> or <see cref="LinqQueryTranslator"/>.
/// </summary>
public partial class QueryBuilder
{
    private QueryPreview _preview = QueryPreview.Text;
    private bool _importing;
    private bool _importFailed;
    private string? _import;

    [Inject] private IJSRuntime Js { get; set; } = null!;

    [Parameter, EditorRequired] public IReadOnlyList<QueryField> Fields { get; set; } = [];

    /// <summary>The query being edited; the builder changes it in place and then raises <see cref="ValueChanged"/>.</summary>
    [Parameter] public QueryGroup Value { get; set; } = new();

    [Parameter] public EventCallback<QueryGroup> ValueChanged { get; set; }

    /// <summary>Translates the builder's English texts and operator names.</summary>
    [Parameter] public Func<string, string>? Localize { get; set; }

    [Parameter] public bool ShowPreview { get; set; } = true;

    [Parameter] public bool ShowImportExport { get; set; } = true;

    /// <summary>How deep groups nest; 0 allows rules only.</summary>
    [Parameter] public int MaxDepth { get; set; } = 3;

    [Parameter] public bool Dense { get; set; } = true;

    /// <summary>"Now" for relative dates such as "today" and "in the last 7 days"; defaults to the local clock.</summary>
    [Parameter] public Func<DateTimeOffset>? Now { get; set; }

    private string T(string text) => Localize?.Invoke(text) ?? text;

    private string PreviewText => _preview switch
    {
        QueryPreview.OData => ODataQueryTranslator.ToFilter(Value, Fields, Now?.Invoke()) ?? string.Empty,
        QueryPreview.Json => QueryJson.Serialize(Value, indented: true),
        _ => Value.IsEmpty ? T("Everything") : QueryDescriber.Describe(Value, Fields, T),
    };

    private string ExportHref => "data:application/json;charset=utf-8," + Uri.EscapeDataString(QueryJson.Serialize(Value, indented: true));

    private Task ChangedAsync() => ValueChanged.InvokeAsync(Value);

    private async Task ImportAsync()
    {
        if (QueryJson.Deserialize(_import) is not { } query)
        {
            _importFailed = true;
            return;
        }

        (_importFailed, _importing, _import) = (false, false, null);
        Value.Logic = query.Logic;
        Value.Not = query.Not;
        Value.Children = query.Children;
        await ChangedAsync();
    }

    private async Task CopyAsync()
    {
        try
        {
            await Js.InvokeVoidAsync("navigator.clipboard.writeText", PreviewText);
        }
        catch (JSException)
        {
            // ponytail: no clipboard (insecure origin, denied permission) - the preview stays selectable
        }
    }

    private enum QueryPreview
    {
        Text,
        OData,
        Json,
    }
}

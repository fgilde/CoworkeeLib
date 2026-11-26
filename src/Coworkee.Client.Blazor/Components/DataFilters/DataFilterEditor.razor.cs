using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.DataFilters;
using Coworkee.Contracts.Query;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.DataFilters;

/// <summary>
/// One data filter of a set and layer: the query builder over the set's properties and the switch for single-row reads. The server validates on save;
/// its problem shows here, as does the problem of a stored filter that no longer parses. Saves the query as the builder's JSON, so it can be edited again.
/// </summary>
public partial class DataFilterEditor
{
    private IReadOnlyList<QueryField> _fields = [];
    private HashSet<string> _protected = [];
    private QueryGroup _query = new();
    private bool _appliesToDetails = true;
    private string? _problem;
    private string? _loadedSet;
    private DataFilterDto? _loaded;
    private bool _busy;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public string EntitySet { get; set; } = string.Empty;

    [Parameter, EditorRequired] public DataFilterLayer Layer { get; set; }

    /// <summary>The group or role; null for System and Tenant.</summary>
    [Parameter] public Guid? LayerKey { get; set; }

    /// <summary>The stored filter; null while none exists (saving creates it).</summary>
    [Parameter] public DataFilterDto? Filter { get; set; }

    /// <summary>Raised after a filter was saved or removed.</summary>
    [Parameter] public EventCallback Changed { get; set; }

    private bool UsesProtected => _query.Rules().Any(r => r.Field is { } name && _protected.Contains(name));

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedSet != EntitySet)
        {
            _loadedSet = EntitySet;
            var fields = await Api.GetDataFilterFieldsAsync(EntitySet);
            _protected = [.. fields.Where(f => f.Protected).Select(f => f.Name)];
            _fields = [.. fields.Select(f => new QueryField(f.Name, f.Protected ? L["{0} (only some users see it)", f.Name] : f.Name, f.Type)
            {
                Nullable = f.Nullable,
                Flags = f.Flags,
                Options = f.Options,
            })];
        }

        // a reload that brings the same filter keeps what the person is typing
        if (!Equals(_loaded, Filter))
        {
            _loaded = Filter;
            _query = Filter is null ? new QueryGroup() : QueryJson.Deserialize(Filter.Query) ?? new QueryGroup();
            _appliesToDetails = Filter?.AppliesToDetails ?? true;
            _problem = null;
        }
    }

    private void QueryChanged(QueryGroup query) => _query = query;

    private async Task SaveAsync()
    {
        _busy = true;
        try
        {
            await Api.SaveDataFilterAsync(new SaveDataFilterRequest(EntitySet, Layer, LayerKey, QueryJson.Serialize(_query), _appliesToDetails));
            _problem = null;
            Snackbar.Add(L["Filter saved"], Severity.Success);
            await Changed.InvokeAsync();
        }
        catch (ApiException exception)
        {
            _problem = exception.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task RemoveAsync()
    {
        if (Filter is not null && await Snackbar.RunAsync(() => Api.DeleteDataFilterAsync(Filter.Id), L["Filter removed"]))
        {
            await Changed.InvokeAsync();
        }
    }
}

using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Components.Groups;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Data.Admin;
using Coworkee.Client.Blazor.Localization;
using System.Text.Json;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Users;

/// <summary>
/// Chooses users from the Users set with the rights, facets and search of the users page, narrowed to roles (direct or through a group) and groups
/// (with their sub-groups) where the caller may read them: everyone the filter matches, single users left out or added by hand. The value is a serialisable <see cref="UserSelection"/> (filter + added + left out) that the server resolves with the caller's rights, e.g.
/// <c>EventTarget.Users(selection)</c>; the recipient count comes from the server and follows changes of users live.
/// </summary>
public partial class CoworkeeUserSelection : IDisposable
{
    private static readonly string[] SearchFields = [nameof(UserRow.Email), nameof(UserRow.FirstName), nameof(UserRow.LastName)];
    private CoworkeeDataTable<UserRow> _table = null!;
    private UserSelectionCountDto? _count;
    private Dictionary<Guid, string> _names = [];
    private CancellationTokenSource? _counting;
    private int _pickerKey;
    private IReadOnlyList<FacetGroupDto> _facets = [];

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private IODataClient OData { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public UserSelection Value { get; set; } = new();

    [Parameter] public EventCallback<UserSelection> ValueChanged { get; set; }

    /// <summary>The count of the current value, after each change.</summary>
    [Parameter] public EventCallback<UserSelectionCountDto> CountChanged { get; set; }

    /// <summary>An OData filter every chosen user matches (e.g. <c>IsActive eq true</c>); part of the selection's filter.</summary>
    [Parameter] public string? Filter { get; set; }

    [Parameter] public int PageSize { get; set; } = 10;

    [Parameter] public string? Class { get; set; }

    /// <summary>The most groups the group filter offers (ordered by path).</summary>
    internal const int MaxGroups = 500;

    /// <summary>How long the count waits for further changes.</summary>
    internal static TimeSpan CountDelay { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <summary>The facets "Roles" (members directly or through a group that has the role, as permissions count roles) and "Groups" (members with their sub-groups).</summary>
    internal static IReadOnlyList<FacetGroupDto> RoleAndGroupFacets(IReadOnlyList<RoleDto> roles, IReadOnlyList<GroupRow> groups)
    {
        var facets = new List<FacetGroupDto>(2);
        if (roles.Count > 0)
        {
            facets.Add(Facet("Roles", "Roles", roles.Select(role =>
            {
                var direct = $"Roles/any(r: r/RoleId eq {role.Id})";
                var through = groups.Where(g => g.Roles.Any(r => r.RoleId == role.Id)).Select(g => g.Id).ToList();
                return (role.Id, role.Name, through.Count == 0 ? direct : $"({direct} or Groups/any(m: m/GroupId in ({string.Join(",", through)})))");
            }), 1000));
        }

        if (groups.Count > 0)
        {
            var paths = groups.Select(g => (g.Code, g.Name)).ToList();
            facets.Add(Facet("Groups", "Groups", groups.Select(group =>
            {
                var below = groups.Where(g => g.Id == group.Id || (group.Code is { } code && g.Code?.StartsWith(code + ".", StringComparison.Ordinal) == true)).Select(g => g.Id);
                return (group.Id, GroupPaths.Path(group.Code, group.Name, paths), $"Groups/any(m: m/GroupId in ({string.Join(",", below)}))");
            }), 1001) with
            {
                TreeSeparator = " / ",
                View = groups.Any(g => g.Code?.Contains('.') == true) ? FacetView.Tree : FacetView.Auto,
            });
        }

        return facets;

        // no counts: the server cannot count through these collections (a negative count shows none)
        static FacetGroupDto Facet(string key, string label, IEnumerable<(Guid Id, string Label, string OData)> options, int order) =>
            new(key, label, key, FacetKind.CheckboxList, FacetOperator.Or,
                [.. options.Select(o => new FacetOptionDto(JsonSerializer.SerializeToElement(o.Id), o.Label, true, -1, o.OData))], true, order)
            {
                Combine = FacetCombine.Any,
            };
    }

    // only what the caller may read: without the right to view roles or groups the filter is not offered
    protected override async Task OnInitializedAsync()
    {
        IReadOnlyList<RoleDto> roles = [];
        IReadOnlyList<GroupRow> groups = [];
        try
        {
            roles = [.. (await Api.GetRolesAsync()).OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)];
        }
        catch (Exception exception) when (exception is HttpRequestException or ApiException)
        {
        }

        try
        {
            groups = (await OData.QueryAsync<GroupRow>("Groups", new ODataQuery { Select = "Id,Name,Code,Roles", OrderBy = "Code", Top = MaxGroups, Count = false }))?.Items ?? [];
        }
        catch (Exception exception) when (exception is HttpRequestException or ApiException)
        {
        }

        _facets = RoleAndGroupFacets(roles, groups);
    }

    private bool IsChosen(Guid id) => Value.Included?.Contains(id) == true || (Value.AllMatching && Value.Excluded?.Contains(id) != true);

    // the table's filter (facets, search, column filters) is the selection's filter; a reload after a change of users counts again
    private async Task LoadedAsync(IReadOnlyList<UserRow> rows)
    {
        if (_table.CurrentFilter != Value.Filter)
        {
            await ChangeAsync(Value with { Filter = _table.CurrentFilter });
        }
        else
        {
            Recount();
        }
    }

    private Task SetAllMatchingAsync(bool all) => ChangeAsync(Value with { AllMatching = all, Excluded = all ? Value.Excluded : null });

    private Task ChooseAsync(Guid id, bool chosen)
    {
        var included = (Value.Included ?? []).Where(x => x != id).ToList();
        var excluded = (Value.Excluded ?? []).Where(x => x != id).ToList();
        if (chosen && !Value.AllMatching)
        {
            included.Add(id);
        }
        else if (!chosen && Value.AllMatching)
        {
            excluded.Add(id);
        }

        return ChangeAsync(Value with { Included = included.Count == 0 ? null : included, Excluded = excluded.Count == 0 ? null : excluded });
    }

    private async Task AddAsync(Guid id)
    {
        _pickerKey++;
        if (id == Guid.Empty || Value.Included?.Contains(id) == true)
        {
            return;
        }

        var excluded = Value.Excluded?.Where(x => x != id).ToList();
        await ChangeAsync(Value with { Included = [.. Value.Included ?? [], id], Excluded = excluded is { Count: > 0 } ? excluded : null });
    }

    private Task ClearExcludedAsync() => ChangeAsync(Value with { Excluded = null });

    private async Task ChangeAsync(UserSelection value)
    {
        Value = value;
        await ValueChanged.InvokeAsync(value);
        if (value.Included is { Count: > 0 } included && included.Any(id => !_names.ContainsKey(id)))
        {
            await Snackbar.RunAsync(async () => _names = new Dictionary<Guid, string>(await Api.GetUserNamesAsync([.. included])));
        }

        Recount();
    }

    // in the background: the table and the checkboxes do not wait for the count
    private void Recount()
    {
        _counting?.Cancel();
        var counting = _counting = new CancellationTokenSource();
        _ = InvokeAsync(async () =>
        {
            try
            {
                await Task.Delay(CountDelay, counting.Token);
                if (Value.IsEmpty)
                {
                    _count = new UserSelectionCountDto(0, 0);
                }
                else if (!await Snackbar.RunAsync(async () => _count = await Api.CountUserSelectionAsync(Value, counting.Token)))
                {
                    return;
                }

                await CountChanged.InvokeAsync(_count);
                StateHasChanged();
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    public void Dispose() => _counting?.Cancel();
}

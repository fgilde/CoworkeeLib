using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Data.Admin;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Users;

/// <summary>
/// Chooses users from the Users set with the rights, facets and search of the users page: everyone the filter matches, single users left out or added
/// by hand. The value is a serialisable <see cref="UserSelection"/> (filter + added + left out) that the server resolves with the caller's rights, e.g.
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

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public UserSelection Value { get; set; } = new();

    [Parameter] public EventCallback<UserSelection> ValueChanged { get; set; }

    /// <summary>The count of the current value, after each change.</summary>
    [Parameter] public EventCallback<UserSelectionCountDto> CountChanged { get; set; }

    /// <summary>An OData filter every chosen user matches (e.g. <c>IsActive eq true</c>); part of the selection's filter.</summary>
    [Parameter] public string? Filter { get; set; }

    [Parameter] public int PageSize { get; set; } = 10;

    [Parameter] public string? Class { get; set; }

    /// <summary>How long the count waits for further changes.</summary>
    internal static TimeSpan CountDelay { get; set; } = TimeSpan.FromMilliseconds(300);

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

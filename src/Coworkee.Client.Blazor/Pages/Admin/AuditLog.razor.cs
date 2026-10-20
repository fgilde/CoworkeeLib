using System.Globalization;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.History;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Auditing;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class AuditLog
{
    private static readonly string[] SearchFields = [nameof(AuditEntryDto.EntityType), nameof(AuditEntryDto.EntityId)];
    private CoworkeeDataTable<AuditEntryDto> _table = null!;
    private string? _me;
    private bool _mine = true;
    private DateRange? _range;
    private Dictionary<string, EntityHistorySetDto> _sets = [];

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private IEntityHistoryApi HistoryApi { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = null!;

    private string? Filter => ODataFilter.And(
        _mine && _me is not null ? $"{nameof(AuditEntryDto.ActorId)} eq {_me}" : null,
        _range?.Start is { } from ? $"{nameof(AuditEntryDto.OccurredAt)} ge {Literal(from.Date)}" : null,
        _range?.End is { } to ? $"{nameof(AuditEntryDto.OccurredAt)} lt {Literal(to.Date.AddDays(1))}" : null);

    protected override async Task OnInitializedAsync()
    {
        _me = ClientUser.From((await AuthenticationState).User).UserId?.ToString();
        try
        {
            _sets = (await HistoryApi.GetSetsAsync() ?? []).ToDictionary(s => s.EntityType, StringComparer.Ordinal);
        }
        catch (ApiException)
        {
            // no history actions then; the log itself still works
        }
    }

    /// <summary>The history set and record of an entry, when the server registered one; hard deleted records are gone.</summary>
    private (EntityHistorySetDto Set, Guid Id)? Record(AuditEntryDto entry) =>
        _sets.TryGetValue(entry.EntityType, out var set) && Guid.TryParse(entry.EntityId, out var id) && (set.SoftDelete || entry.Action != "Deleted")
            ? (set, id)
            : null;

    private Task OpenHistoryAsync(AuditEntryDto entry, Guid id) =>
        EntityHistoryDialog.ShowAsync(Dialogs, $"{entry.EntityType} {entry.EntityId}", _sets[entry.EntityType].EntitySet, id, EventCallback.Factory.Create(this, ReloadAsync));

    private async Task RestoreAsync(AuditEntryDto entry, (EntityHistorySetDto Set, Guid Id) record, EntityHistoryRestoreMode mode)
    {
        if (await EntityHistoryRestoreDialog.ConfirmAsync(Dialogs, HistoryApi, Snackbar, L, record.Set.EntitySet, record.Id,
                new EntityHistoryRestoreRequest(entry.Id, Mode: mode), p => CoworkeeEntityHistory.DefaultLabel(L, p)))
        {
            await ReloadAsync();
        }
    }

    private static string Literal(DateTime localDay) =>
        new DateTimeOffset(DateTime.SpecifyKind(localDay, DateTimeKind.Local)).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string Utc(DateTimeOffset at) => "UTC " + at.UtcDateTime.ToString("G", CultureInfo.CurrentCulture);

    private static string Summary(AuditEntryDto entry) =>
        string.Join(", ", entry.Changes.Take(3).Select(c => c.Property)) + (entry.Changes.Count > 3 ? " …" : string.Empty);

    private static Color ActionColor(string action) => action switch
    {
        "Created" => Color.Success,
        "Deleted" => Color.Error,
        "Restored" => Color.Info,
        _ => Color.Primary,
    };

    private static string ActionIcon(string action) => action switch
    {
        "Created" => Icons.Material.Outlined.AddCircleOutline,
        "Deleted" => Icons.Material.Outlined.DeleteOutline,
        "Restored" => Icons.Material.Outlined.Restore,
        _ => Icons.Material.Outlined.Edit,
    };

    private Task MineAsync(bool mine)
    {
        _mine = mine;
        return ReloadAsync();
    }

    private Task RangeAsync(DateRange? range)
    {
        _range = range;
        return ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        StateHasChanged();
        await Task.Yield();
        await _table.ReloadAsync();
    }
}

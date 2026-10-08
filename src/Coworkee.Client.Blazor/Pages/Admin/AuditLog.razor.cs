using System.Globalization;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Localization;
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

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = null!;

    private string? Filter => ODataFilter.And(
        _mine && _me is not null ? $"{nameof(AuditEntryDto.ActorId)} eq {_me}" : null,
        _range?.Start is { } from ? $"{nameof(AuditEntryDto.OccurredAt)} ge {Literal(from.Date)}" : null,
        _range?.End is { } to ? $"{nameof(AuditEntryDto.OccurredAt)} lt {Literal(to.Date.AddDays(1))}" : null);

    protected override async Task OnInitializedAsync() => _me = (await AuthenticationState).User.FindFirst("sub")?.Value;

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

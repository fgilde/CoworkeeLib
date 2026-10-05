using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Auditing;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class AuditLog
{
    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    private MudTable<AuditEntryDto>? _table;
    private string? _entityType;
    private string? _entityId;
    private DateTime? _from;
    private DateTime? _to;
    private Guid? _expanded;

    private async Task<TableData<AuditEntryDto>> LoadAsync(TableState state, CancellationToken cancellationToken)
    {
        var page = await Api.GetAuditAsync(new AuditQuery(
            _entityType, _entityId, null,
            _from is { } from ? new DateTimeOffset(from.Date) : null,
            _to is { } to ? new DateTimeOffset(to.Date.AddDays(1)) : null,
            state.Page + 1, state.PageSize), cancellationToken);
        return new TableData<AuditEntryDto> { Items = page.Items, TotalItems = page.TotalCount };
    }

    private void Toggle(AuditEntryDto entry) => _expanded = _expanded == entry.Id ? null : entry.Id;

    private async Task FilterAsync(Action apply)
    {
        apply();
        await _table!.ReloadServerData();
    }
}

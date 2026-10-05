using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts;
using Coworkee.Contracts.Mailing;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class MailLog
{
    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    private MudTable<OutgoingMailDto>? _table;
    private OutgoingMailStatus? _status;
    private string? _search;

    private async Task<TableData<OutgoingMailDto>> LoadAsync(TableState state, CancellationToken cancellationToken)
    {
        var page = await Api.GetOutgoingMailsAsync(new PageRequest(state.Page + 1, state.PageSize, _search), _status, cancellationToken);
        return new TableData<OutgoingMailDto> { Items = page.Items, TotalItems = page.TotalCount };
    }

    private async Task StatusChangedAsync(OutgoingMailStatus? status)
    {
        _status = status;
        await _table!.ReloadServerData();
    }

    private static Color ColorFor(OutgoingMailStatus status) => status switch
    {
        OutgoingMailStatus.Sent => Color.Success,
        OutgoingMailStatus.Failed => Color.Error,
        OutgoingMailStatus.Skipped => Color.Warning,
        _ => Color.Default,
    };
}

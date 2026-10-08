using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Auditing;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class AuditLog
{
    private static readonly string[] SearchFields = [nameof(AuditEntryDto.EntityType), nameof(AuditEntryDto.EntityId)];
    private IReadOnlyDictionary<Guid, string> _names = new Dictionary<Guid, string>();

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private async Task LoadNamesAsync(IReadOnlyList<AuditEntryDto> entries)
    {
        var ids = entries.Select(e => e.ActorId).OfType<Guid>().Distinct().ToList();
        if (ids.Count > 0)
        {
            await Snackbar.RunAsync(async () => _names = await Api.GetUserNamesAsync(ids));
        }
    }

    private Task ShowChangesAsync(AuditEntryDto entry) =>
        Dialogs.ShowAsync<AuditChangesDialog>(L["Changes"], new DialogParameters<AuditChangesDialog> { { d => d.Changes, entry.Changes } },
            new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true, CloseOnEscapeKey = true });
}

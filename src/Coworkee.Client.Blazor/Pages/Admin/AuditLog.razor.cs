using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Auditing;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class AuditLog
{
    private static readonly string[] SearchFields = [nameof(AuditEntryDto.EntityType), nameof(AuditEntryDto.EntityId)];

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    private Task ShowChangesAsync(AuditEntryDto entry) =>
        Dialogs.ShowSideSheetAsync<AuditChangesDialog>(L["Changes"], new DialogParameters<AuditChangesDialog> { { d => d.Changes, entry.Changes } });
}

using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Mailing;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class MailLog
{
    private static readonly string[] SearchFields = [nameof(OutgoingMailDto.To), nameof(OutgoingMailDto.Subject)];

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    private static Color ColorFor(OutgoingMailStatus status) => status switch
    {
        OutgoingMailStatus.Sent => Color.Success,
        OutgoingMailStatus.Failed => Color.Error,
        _ => Color.Default,
    };
}

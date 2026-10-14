using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Mailing;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Mail;

public partial class MailVariablesPanel
{
    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public IReadOnlyList<MailVariableDto> Variables { get; set; } = [];

    [Parameter] public EventCallback<MailVariableDto> OnPick { get; set; }
}

using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Mailing;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class MailTemplates
{
    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    private IReadOnlyList<MailTemplateSummaryDto> _templates = [];

    protected override async Task OnInitializedAsync() => _templates = await Api.GetMailTemplatesAsync();
}

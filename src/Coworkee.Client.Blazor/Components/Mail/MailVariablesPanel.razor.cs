using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Mailing;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Mail;

/// <summary>The placeholders of a template with type and sample value; a click inserts one.</summary>
public partial class MailVariablesPanel
{
    private string? _filter;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public IReadOnlyList<MailVariableDto> Variables { get; set; } = [];

    [Parameter] public EventCallback<MailVariableDto> OnPick { get; set; }

    [Parameter] public string Height { get; set; } = "auto";

    private IEnumerable<MailVariableDto> Shown => string.IsNullOrWhiteSpace(_filter)
        ? Variables
        : Variables.Where(v => v.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase));
}

using Coworkee.Contracts.Auditing;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components;

public partial class AuditChangesDialog
{
    [Parameter, EditorRequired] public IReadOnlyList<AuditChangeDto> Changes { get; set; } = [];
}

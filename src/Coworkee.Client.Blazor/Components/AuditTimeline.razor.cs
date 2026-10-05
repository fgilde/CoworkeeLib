using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Auditing;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components;

public partial class AuditTimeline
{
    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    private IReadOnlyList<AuditEntryDto>? _entries;

    [Parameter, EditorRequired] public string EntityType { get; set; } = string.Empty;

    [Parameter, EditorRequired] public string EntityId { get; set; } = string.Empty;

    protected override async Task OnParametersSetAsync() => await ReloadAsync();

    public async Task ReloadAsync() =>
        _entries = (await Api.GetAuditAsync(new AuditQuery(EntityType, EntityId, PageSize: 50))).Items;
}

using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

/// <summary>
/// Keeps a detail page in step with its [Realtime] entity: a change in another session calls <see cref="OnChanged"/>, a deletion
/// says so and goes back to <see cref="ReturnHref"/>.
/// </summary>
public partial class EntityWatch
{
    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    /// <summary>The entity class name, e.g. "Brand".</summary>
    [Parameter, EditorRequired] public string EntityType { get; set; } = string.Empty;

    [Parameter, EditorRequired] public object EntityId { get; set; } = string.Empty;

    [Parameter, EditorRequired] public string ReturnHref { get; set; } = "/";

    [Parameter] public EventCallback OnChanged { get; set; }

    private async Task ReceiveAsync(RealtimeEnvelope envelope)
    {
        if (EntityChangedPayload.From(envelope) is not { } change)
        {
            return;
        }

        if (change.IsDeleted)
        {
            Snackbar.Add(L["This item was deleted in the meantime."], Severity.Warning);
            Nav.NavigateTo(ReturnHref);
            return;
        }

        await OnChanged.InvokeAsync();
    }
}

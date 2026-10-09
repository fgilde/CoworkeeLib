using Coworkee.Client.Blazor.Features;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components;

/// <summary>Shows its content only while the bool feature is on for the current tenant.</summary>
public partial class FeatureGate : IDisposable
{
    private bool _enabled;

    [Inject] private FeatureStore Features { get; set; } = null!;

    [Parameter, EditorRequired] public string Feature { get; set; } = string.Empty;

    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter] public RenderFragment? Disabled { get; set; }

    protected override void OnInitialized() => Features.Changed += Reload;

    protected override async Task OnParametersSetAsync() => _enabled = await Features.IsEnabledAsync(Feature);

    public void Dispose() => Features.Changed -= Reload;

    private void Reload() => InvokeAsync(async () =>
    {
        _enabled = await Features.IsEnabledAsync(Feature);
        StateHasChanged();
    });
}

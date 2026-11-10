using Coworkee.Client.Blazor.Theming;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components;

public partial class CoworkeeBrand : IDisposable
{
    [Inject] private CoworkeeClientOptions Options { get; set; } = null!;

    /// <summary>At the head of the navigation: <see cref="Theming.CoworkeeTheme.ShowAppNameInNav"/> decides about the name instead of the app bar's option.</summary>
    [Parameter] public bool InNav { get; set; }

    [Inject] private ThemeService ThemeService { get; set; } = null!;

    protected override void OnInitialized() => ThemeService.Changed += Refresh;

    public void Dispose() => ThemeService.Changed -= Refresh;

    private void Refresh() => InvokeAsync(StateHasChanged);
}

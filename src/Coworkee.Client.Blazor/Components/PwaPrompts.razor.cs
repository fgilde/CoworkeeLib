using Coworkee.Client.Blazor.Layout;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

public partial class PwaPrompts : IDisposable
{
    private const string InstallHinted = "coworkee.install-hinted";

    private bool _updateShown;
    private bool _installShown;

    [Inject] private PwaService Pwa { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private IJSRuntime JS { get; set; } = null!;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    protected override void OnInitialized() => Pwa.Changed += Prompt;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await Pwa.StartAsync();
        }
    }

    public void Dispose() => Pwa.Changed -= Prompt;

    private void Prompt() => InvokeAsync(async () =>
    {
        if (Pwa.UpdateReady && !_updateShown)
        {
            _updateShown = true;
            Snackbar.Add(L["A new version of the app is available."], Severity.Info, options =>
            {
                options.RequireInteraction = true;
                options.Action = L["Reload"];
                options.ActionColor = Color.Primary;
                options.OnClick = _ => Pwa.ApplyUpdateAsync();
            }, "coworkee-update");
        }

        if (Pwa.CanInstall && !_installShown && !await HintedAsync())
        {
            _installShown = true;
            Snackbar.Add(L["Install the app on this device to open it like any other app."], Severity.Normal, options =>
            {
                options.VisibleStateDuration = 15000;
                options.Action = L["Install app"];
                options.ActionColor = Color.Primary;
                options.OnClick = _ => Pwa.InstallAsync();
            }, "coworkee-install");
        }
    });

    // the hint comes once per browser; the user menu keeps offering the installation
    private async Task<bool> HintedAsync()
    {
        try
        {
            if (await JS.InvokeAsync<string?>("localStorage.getItem", InstallHinted) is not null)
            {
                return true;
            }

            await JS.InvokeVoidAsync("localStorage.setItem", InstallHinted, "1");
        }
        catch (JSException)
        {
        }

        return false;
    }
}

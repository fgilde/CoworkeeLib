using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages;

public partial class ProfilePrivacy
{
    private string? _email;
    private string? _confirmation;
    private bool _busy;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private FileDownloader Downloader { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    private bool Confirmed => _email is not null && string.Equals(_confirmation?.Trim(), _email, StringComparison.OrdinalIgnoreCase);

    protected override async Task OnInitializedAsync() => _email = (await Api.GetMyProfileAsync()).Email;

    private Task DownloadAsync() => RunAsync(async () => await Downloader.DownloadAsync("personal-data.json", "application/json", await Api.ExportMyPersonalDataAsync()));

    private Task DeleteAsync() => RunAsync(async () =>
    {
        await Api.DeleteMyAccountAsync(_confirmation!.Trim());
        var logout = await Api.LogoutAsync();
        Nav.NavigateTo(logout.Redirect, forceLoad: true);
    });

    private async Task RunAsync(Func<Task> action)
    {
        _busy = true;
        try
        {
            await Snackbar.RunAsync(action);
        }
        finally
        {
            _busy = false;
        }
    }
}

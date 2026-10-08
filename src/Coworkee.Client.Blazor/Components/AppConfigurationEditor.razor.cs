using Coworkee.Client.Blazor.Components.Data;
using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Settings;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

public partial class AppConfigurationEditor<T>
    where T : class, new()
{
    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Parameter, EditorRequired]
    public ClientAppConfiguration Registration { get; set; } = null!;

    private T? _value;
    private IReadOnlyList<string> _changed = [];
    private string? _error;
    private bool _busy;
    private int _revision;

    protected override async Task OnParametersSetAsync() => await LoadAsync(() => Api.GetAppConfigurationAsync(Registration.Section));

    private async Task LoadAsync(Func<Task<AppConfigurationValuesDto>> call)
    {
        _busy = true;
        _error = null;
        try
        {
            var section = await call();
            _value = section.Values.Deserialize<T>(JsonSerializerOptions.Web) ?? new T();
            _changed = section.ChangedKeys;
            _revision++;
        }
        catch (ApiException exception)
        {
            _error = exception.Status == 403 ? "The app configuration is managed from the system organisation." : exception.Message;
        }
        finally
        {
            _busy = false;
            StateHasChanged();
        }
    }

    public async Task SaveAsync()
    {
        if (_value is null)
        {
            return;
        }

        var values = JsonSerializer.SerializeToElement(_value, JsonSerializerOptions.Web);
        await LoadAsync(() => Api.SaveAppConfigurationAsync(Registration.Section, values));
        if (_error is null)
        {
            Snackbar.Add($"{Registration.Title} saved. Some values only apply after the services restart.", Severity.Success);
        }
    }

    private async Task ResetAsync()
    {
        if (await Dialogs.ConfirmAsync("Restore defaults", "All changes made here go away; the configured values apply again.", "Restore", "Cancel", Icons.Material.Outlined.SettingsBackupRestore))
        {
            await LoadAsync(() => Api.ResetAppConfigurationAsync(Registration.Section));
        }
    }
}

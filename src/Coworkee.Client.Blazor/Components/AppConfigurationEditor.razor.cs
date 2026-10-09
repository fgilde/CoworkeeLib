using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Settings;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;

namespace Coworkee.Client.Blazor.Components;

public partial class AppConfigurationEditor<T>
    where T : class, new()
{
    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired]
    public ClientAppConfiguration Registration { get; set; } = null!;

    private T? _value;
    private ObjectEditMeta<T>? _meta;
    private IReadOnlyList<string> _changed = [];
    private IReadOnlyList<string> _locked = [];
    private IReadOnlyList<string> _hidden = [];
    private string? _error;
    private bool _busy;
    private int _revision;

    protected override async Task OnParametersSetAsync() => await LoadAsync(() => Api.GetAppConfigurationAsync(Registration.Section));

    /// <summary>The meta of the registration, then what the server locks or hides (a locked object locks all its properties).</summary>
    private void Configure(ObjectEditMeta<T> meta)
    {
        (Registration.Meta as Action<ObjectEditMeta<T>>)?.Invoke(meta);
        foreach (var property in Properties(meta, _locked))
        {
            property.AsReadOnly();
        }

        foreach (var property in Properties(meta, _hidden))
        {
            property.Ignore();
        }
    }

    // the server names properties like configuration keys ("Jobs:WorkerCount"), the form with dots
    private static IEnumerable<ObjectEditPropertyMeta> Properties(ObjectEditMeta<T> meta, IReadOnlyList<string> paths) =>
        paths.Select(p => meta.Property(p.Replace(':', '.'))).OfType<ObjectEditPropertyMeta>().SelectMany(WithChildren);

    private static IEnumerable<ObjectEditPropertyMeta> WithChildren(ObjectEditPropertyMeta property) =>
        [property, .. property.Children?.SelectMany(WithChildren) ?? []];

    private async Task LoadAsync(Func<Task<AppConfigurationValuesDto>> call)
    {
        _busy = true;
        _error = null;
        try
        {
            var section = await call();
            _value = section.Values.Deserialize<T>(JsonSerializerOptions.Web) ?? new T();
            _changed = section.ChangedKeys;
            _locked = section.Locked ?? [];
            _hidden = section.Hidden ?? [];
            _meta = _value.ObjectEditMeta(Configure); // MudEx applies a MetaConfiguration only after its editors took their labels
            _revision++;
        }
        catch (ApiException exception)
        {
            _error = exception.Status == 403 ? L["The app configuration is managed from the system organisation."] : exception.Message;
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
            Snackbar.Add(L["{0} saved. Some values only apply after the services restart.", L[Registration.Title]], Severity.Success);
        }
    }

    private async Task ResetAsync()
    {
        if (await Dialogs.ConfirmAsync(L["Restore defaults"], L["All changes made here go away; the configured values apply again."], L["Restore"], L["Cancel"], Icons.Material.Outlined.SettingsBackupRestore))
        {
            await LoadAsync(() => Api.ResetAppConfigurationAsync(Registration.Section));
        }
    }
}

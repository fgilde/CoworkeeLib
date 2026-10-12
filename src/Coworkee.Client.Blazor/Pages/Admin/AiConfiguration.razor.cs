using Coworkee.Client.Blazor.Ai;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Ai;
using Coworkee.Contracts.Settings;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>Provider, key, model and options of the assistant, saved as the system wide AI settings.</summary>
public partial class AiConfiguration
{
    private AiConnectionForm? _form;
    private AiConnectionForm? _saved;
    private IReadOnlyList<AiModelDto> _models = [];
    private string? _modelError;
    private AiConnectionTestDto? _test;
    private bool _busy;
    private bool _testing;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private IAiConnectionApi Connections { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private string Status => _saved switch
    {
        { Enabled: true, IsComplete: true } => "active",
        { Enabled: true } => "incomplete",
        _ => "off",
    };

    private string StatusText => Status switch
    {
        "active" => "Active",
        "incomplete" => "Incomplete",
        _ => "Off",
    };

    private Color StatusColor => Status switch
    {
        "active" => Color.Success,
        "incomplete" => Color.Warning,
        _ => Color.Default,
    };

    private string StatusIcon => Status == "active" ? Icons.Material.Outlined.CheckCircle : Icons.Material.Outlined.PowerSettingsNew;

    private string ModelHelp => _models.Count > 0
        ? L["{0} models loaded; type any other name.", _models.Count]
        : L[_form!.Info.DefaultModel is null ? "Required. Load the models or type a name." : "Empty: the default. Load the models or type a name."];

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        await Snackbar.RunAsync(async () =>
        {
            var values = await Api.GetSettingsAsync(SettingScope.Global);
            _form = AiConnectionForm.From(values);
            _saved = AiConnectionForm.From(values);
        });
        _models = [];
        _modelError = null;
        _test = null;
    }

    private void SelectProvider(string provider)
    {
        _form!.Provider = provider;
        _form.Model = null;
        _form.Endpoint = null;
        _models = [];
        _modelError = null;
        _test = null;
    }

    private Task<IEnumerable<string>> SearchModels(string? text, CancellationToken cancellationToken)
    {
        var all = _models.Select(m => m.Id).Concat(_form!.Info.Models).Distinct(StringComparer.Ordinal);
        return Task.FromResult(string.IsNullOrWhiteSpace(text) ? all : all.Where(m => m.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase)));
    }

    private Task LoadModelsAsync() => RunAsync(async () =>
    {
        var result = await Connections.ListModelsAsync(_form!.ToDraft());
        _models = result.Models;
        _modelError = result.Error;
    });

    private async Task TestAsync()
    {
        _testing = true;
        await RunAsync(async () => _test = await Connections.TestAsync(_form!.ToDraft()));
        _testing = false;
    }

    private async Task SaveAsync()
    {
        _busy = true;
        if (await Snackbar.RunAsync(() => Api.SetSettingsAsync(SettingScope.Global, _form!.ToSettings()), L["Saved"]))
        {
            var test = _test;
            await LoadAsync();
            _test = test;
        }

        _busy = false;
    }

    private async Task RunAsync(Func<Task> action)
    {
        _busy = true;
        await Snackbar.RunAsync(action);
        _busy = false;
    }
}

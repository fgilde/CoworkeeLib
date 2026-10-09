using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class Scopes
{
    private IReadOnlyList<ScopeDto> _scopes = [];
    private bool _busy;

    [Inject] private IClientsApi Api { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    protected override Task OnInitializedAsync() => RunAsync(() => Task.CompletedTask);

    private async Task CreateAsync()
    {
        if (await Dialogs.ShowEditAsync(L["New scope"], new ScopeForm(), form => Api.CreateScopeAsync(form.ToRequest())))
        {
            await RunAsync(() => Task.CompletedTask);
        }
    }

    private async Task EditAsync(ScopeDto scope)
    {
        var form = new ScopeForm { Name = scope.Name, DisplayName = scope.DisplayName, Description = scope.Description, Resources = string.Join(' ', scope.Resources) };
        if (await Dialogs.ShowEditAsync(L["Edit scope"], form, f => Api.UpdateScopeAsync(scope.Id, f.ToRequest())))
        {
            await RunAsync(() => Task.CompletedTask);
        }
    }

    private async Task DeleteAsync(ScopeDto scope)
    {
        if (await Dialogs.ConfirmAsync(L["Delete"], L["Delete {0}? This cannot be undone.", scope.Name], L["Delete"], L["Cancel"], Icons.Material.Outlined.DeleteForever))
        {
            await RunAsync(() => Api.DeleteScopeAsync(scope.Id));
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        _busy = true;
        try
        {
            await Snackbar.RunAsync(action);
            await Snackbar.RunAsync(async () => _scopes = await Api.GetScopesAsync());
        }
        finally
        {
            _busy = false;
        }
    }

    private sealed class ScopeForm
    {
        public string Name { get; set; } = string.Empty;

        public string? DisplayName { get; set; }

        public string? Description { get; set; }

        /// <summary>Audiences, separated by spaces.</summary>
        public string Resources { get; set; } = string.Empty;

        public ScopeRequest ToRequest() =>
            new(Name, DisplayName, Description, Resources.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}

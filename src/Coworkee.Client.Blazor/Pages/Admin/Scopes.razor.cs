using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Components.Editors;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;

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
        if (await Dialogs.ShowEditAsync(L["New scope"], new ScopeForm(), form => Api.CreateScopeAsync(form.ToRequest()), ResourcesMeta(null)))
        {
            await RunAsync(() => Task.CompletedTask);
        }
    }

    private async Task EditAsync(ScopeDto scope)
    {
        var form = new ScopeForm { Name = scope.Name, DisplayName = scope.DisplayName, Description = scope.Description, Resources = scope.Resources };
        if (await Dialogs.ShowEditAsync(L["Edit scope"], form, f => Api.UpdateScopeAsync(scope.Id, f.ToRequest()), ResourcesMeta(scope.Id)))
        {
            await RunAsync(() => Task.CompletedTask);
        }
    }

#pragma warning disable BL0005 // MudEx configures the rendered editor through this instance
    private Action<ObjectEditMeta<ScopeForm>> ResourcesMeta(Guid? editing) => meta =>
        meta.Property(f => f.Resources).RenderAsChips(e =>
        {
            e.Label = L["Resources"];
            e.HelperText = L["Audiences of the access token, e.g. https://api.example"];
            e.SearchFunc = (text, _) => Task.FromResult(_scopes.Where(s => s.Id != editing).SelectMany(s => s.Resources).Distinct()
                .Where(r => r.Contains(text, StringComparison.OrdinalIgnoreCase)));
        });
#pragma warning restore BL0005

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

        public IReadOnlyList<string> Resources { get; set; } = [];

        public ScopeRequest ToRequest() =>
            new(Name, DisplayName, Description, Resources);
    }
}

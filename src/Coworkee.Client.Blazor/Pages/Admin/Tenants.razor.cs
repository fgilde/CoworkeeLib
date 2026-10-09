using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Data.Admin;
using Coworkee.Client.Blazor.Features;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Features;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class Tenants
{
    private static readonly string[] SearchFields = [nameof(TenantRow.Name), nameof(TenantRow.Identifier)];
    private CoworkeeDataTable<TenantRow> _table = null!;
    private IReadOnlyList<EditionDto> _editions = [];
    private IReadOnlyList<FeatureGroupDto> _groups = [];
    private Dictionary<Guid, TenantDetailsDto> _details = [];

    [Inject] private IFeaturesApi Api { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    protected override async Task OnInitializedAsync() => await Snackbar.RunAsync(async () =>
    {
        _groups = await Api.GetDefinitionsAsync();
        _editions = await Api.GetEditionsAsync();
    });

    private async Task LoadDetailsAsync(IReadOnlyList<TenantRow> tenants) =>
        await Snackbar.RunAsync(async () => _details = (await Api.GetTenantDetailsAsync([.. tenants.Select(t => t.Id)])).ToDictionary(d => d.TenantId));

    private TenantDetailsDto? Details(TenantRow tenant) => _details.GetValueOrDefault(tenant.Id);

    private string EditionName(Guid? id) => _editions.FirstOrDefault(e => e.Id == id)?.Name ?? string.Empty;

    private async Task CreateAsync()
    {
        if (await Dialogs.ShowEditAsync(L["New tenant"], new NewTenant(), t =>
                Api.CreateTenantAsync(new CreateTenantRequest(t.Name, t.Identifier, t.IsActive, t.AcceptsRegistrations, t.AdminEmail, t.AdminPassword)),
                meta => meta.Property(t => t.AdminPassword).WithAdditionalAttribute(nameof(MudTextField<string>.InputType), InputType.Password)))
        {
            await _table.ReloadAsync();
        }
    }

    private async Task EditAsync(TenantRow tenant)
    {
        var form = new TenantForm { Name = tenant.Name, Identifier = tenant.Identifier, IsActive = tenant.IsActive, AcceptsRegistrations = tenant.AcceptsRegistrations };
        if (await Dialogs.ShowEditAsync(L["Edit tenant"], form, t => Api.UpdateTenantAsync(tenant.Id, Request(t))))
        {
            await _table.ReloadAsync();
        }
    }

    private async Task SetActiveAsync(TenantRow tenant, bool active)
    {
        await Snackbar.RunAsync(() => Api.UpdateTenantAsync(tenant.Id, new TenantRequest(tenant.Name, tenant.Identifier, active, tenant.AcceptsRegistrations)));
        await _table.ReloadAsync();
    }

    private async Task SetEditionAsync(TenantRow tenant, Guid? editionId)
    {
        await Snackbar.RunAsync(() => Api.SetTenantFeaturesAsync(tenant.Id, new TenantFeaturesRequest(editionId, Details(tenant)?.Overrides)), L["Edition assigned"]);
        await _table.ReloadAsync();
    }

    private async Task OverrideAsync(TenantRow tenant)
    {
        var details = Details(tenant);
        var edition = _editions.FirstOrDefault(e => e.Id == details?.EditionId);
        var overrides = details?.Overrides ?? new Dictionary<string, string>();
        if (await FeatureValuesDialog.ShowAsync(Dialogs, L["Features of {0}", tenant.Name], _groups, overrides, FeatureValuesDialog.Defaults(_groups, edition?.Values)) is { } values
            && await Snackbar.RunAsync(() => Api.SetTenantFeaturesAsync(tenant.Id, new TenantFeaturesRequest(details?.EditionId, values)), L["Features saved"]))
        {
            await _table.ReloadAsync();
        }
    }

    private static TenantRequest Request(TenantForm form) => new(form.Name, form.Identifier, form.IsActive, form.AcceptsRegistrations);

    private class TenantForm
    {
        public string Name { get; set; } = string.Empty;

        public string Identifier { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public bool AcceptsRegistrations { get; set; }
    }

    private sealed class NewTenant : TenantForm
    {
        public string? AdminEmail { get; set; }

        public string? AdminPassword { get; set; }
    }
}

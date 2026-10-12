using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class ApiKeys
{
    private static readonly string[] SearchFields = [nameof(ApiKeyDto.Name), nameof(ApiKeyDto.Description), nameof(ApiKeyDto.Prefix)];
    private IReadOnlyDictionary<Guid, string> _names = new Dictionary<Guid, string>();
    private CoworkeeDataTable<ApiKeyDto>? _table;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private IApiKeysApi Keys { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private async Task LoadNamesAsync(IReadOnlyList<ApiKeyDto> keys)
    {
        var ids = keys.Select(k => k.UserId).Distinct().ToList();
        if (ids.Count > 0)
        {
            await Snackbar.RunAsync(async () => _names = await Api.GetUserNamesAsync(ids));
        }
    }

    private async Task RevokeAsync(ApiKeyDto key)
    {
        var owner = _names.GetValueOrDefault(key.UserId, key.UserId.ToString());
        if (await Dialogs.ConfirmAsync(L["Revoke API key"], L["Revoke {0} of {1}? Clients using it stop working at once.", key.Name, owner], L["Revoke"], L["Cancel"],
                Icons.Material.Outlined.KeyOff)
            && await Snackbar.RunAsync(() => Keys.RevokeAsync(key.Id), L["{0} revoked", key.Name])
            && _table is not null)
        {
            await _table.ReloadAsync();
        }
    }
}

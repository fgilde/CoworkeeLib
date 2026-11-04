using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>Asks before a custom permission goes, naming the roles, groups and users that lose it.</summary>
public partial class CustomPermissionDeleteDialog
{
    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public CustomPermissionDto Permission { get; set; } = null!;

    [Parameter, EditorRequired] public CustomPermissionUsageDto Usage { get; set; } = null!;

    private IEnumerable<(string Title, IReadOnlyList<string> Names)> Holders =>
        new (string, IReadOnlyList<string>)[] { (L["Roles"], Usage.Roles), (L["Groups"], Usage.Groups), (L["Users"], Usage.Users) }.Where(h => h.Item2.Count > 0);

    public static async Task<bool> ConfirmAsync(IDialogService dialogs, CoworkeeLocalizer l, CustomPermissionDto permission, CustomPermissionUsageDto usage)
    {
        var parameters = new DialogParameters { [nameof(Permission)] = permission, [nameof(Usage)] = usage };
        var dialog = await dialogs.ShowExAsync<CustomPermissionDeleteDialog>(l["Delete permission"], parameters, EditDialogExtensions.Small());
        return await dialog.Result is { Canceled: false };
    }
}

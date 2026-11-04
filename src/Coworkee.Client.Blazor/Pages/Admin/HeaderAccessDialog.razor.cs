using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Security;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>Chooses the permissions a header item needs, in a sheet from the right; the header page saves them with the rest.</summary>
public partial class HeaderAccessDialog
{
    private IReadOnlyList<string> _permissions = [];
    private PermissionMatch _match;

    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public IReadOnlyList<string> Permissions { get; set; } = [];

    [Parameter] public PermissionMatch Match { get; set; }

    protected override void OnInitialized()
    {
        _permissions = Permissions;
        _match = Match;
    }

    private void Apply() => Dialog.Close(DialogResult.Ok((_permissions, _match)));

    /// <summary>The chosen permissions and their match, or null when cancelled.</summary>
    public static async Task<(IReadOnlyList<string> Permissions, PermissionMatch Match)?> EditAsync(
        IDialogService dialogs, string title, IReadOnlyList<string> permissions, PermissionMatch match)
    {
        var parameters = new DialogParameters { [nameof(Permissions)] = permissions, [nameof(Match)] = match };
        var dialog = await dialogs.ShowExAsync<HeaderAccessDialog>(title, parameters, EditDialogExtensions.RightSheet());
        return await dialog.Result is { Canceled: false, Data: ValueTuple<IReadOnlyList<string>, PermissionMatch> chosen } ? chosen : null;
    }
}

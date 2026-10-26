using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions;

namespace Coworkee.Client.Blazor.Components.Groups;

/// <summary>The group a dialog picked; <c>null</c> for the top level.</summary>
public sealed record GroupPick(Guid? Id);

/// <summary>Picks a group in the tree, e.g. the new parent of a group or the group that takes over all members.</summary>
public partial class GroupMoveDialog
{
    private GroupPick? _pick;

    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    /// <summary>The group that cannot be picked.</summary>
    [Parameter] public Guid? Exclude { get; set; }

    /// <summary>Hides the subtree of <see cref="Exclude"/> as well; off, its sub-groups stay pickable.</summary>
    [Parameter] public bool ExcludeSubtree { get; set; } = true;

    /// <summary>Offers the top level besides the groups.</summary>
    [Parameter] public bool AllowRoot { get; set; }

    public static async Task<GroupPick?> PickAsync(IDialogService dialogs, string title, Guid? exclude, bool allowRoot, bool excludeSubtree = true)
    {
        var parameters = new DialogParameters { [nameof(Exclude)] = exclude, [nameof(AllowRoot)] = allowRoot, [nameof(ExcludeSubtree)] = excludeSubtree };
        var dialog = await dialogs.ShowExAsync<GroupMoveDialog>(title, parameters, EditDialogExtensions.Small());
        return await dialog.Result is { Canceled: false, Data: GroupPick pick } ? pick : null;
    }

    /// <summary>Asks for the new parent of a group and moves it there with its subtree; <c>true</c> when it moved.</summary>
    public static async Task<bool> MoveAsync(IDialogService dialogs, ICoworkeeApi api, ISnackbar snackbar, CoworkeeLocalizer l, Guid id, string name) =>
        await PickAsync(dialogs, l["Move {0}", name], id, allowRoot: true) is { } pick
        && await snackbar.RunAsync(() => api.MoveGroupAsync(id, pick.Id), l["Group moved"]);
}

using System.ComponentModel.DataAnnotations;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using MudBlazor;
using MudBlazor.Extensions;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>What the user page and the action menu of the users list do with one user: ask first where it cannot be undone, then call the API.</summary>
internal static class UserActions
{
    /// <returns><c>true</c> when the user was locked.</returns>
    public static async Task<bool> LockAsync(IDialogService dialogs, ICoworkeeApi api, ISnackbar snackbar, CoworkeeLocalizer l, Guid id, string email)
    {
        if (await dialogs.ShowEditAsync(l["Lock {0}", email], new LockForm()) is not { } form)
        {
            return false;
        }

        var until = form.Until is { } date ? new DateTimeOffset(date.Date.AddDays(1), DateTimeOffset.Now.Offset) : (DateTimeOffset?)null;
        return await snackbar.RunAsync(() => api.LockUserAsync(id, until), l["Locked; the user was signed out everywhere."]);
    }

    public static Task<bool> UnlockAsync(ICoworkeeApi api, ISnackbar snackbar, CoworkeeLocalizer l, Guid id) => snackbar.RunAsync(() => api.UnlockUserAsync(id), l["Unlocked"]);

    public static async Task<bool> SignOutAsync(IDialogService dialogs, ICoworkeeApi api, ISnackbar snackbar, CoworkeeLocalizer l, Guid id, string email) =>
        await dialogs.ConfirmAsync(l["Sign out everywhere"], l["End all sessions of {0}? Open windows sign out right away.", email], l["Sign out"], l["Cancel"],
            Icons.Material.Outlined.Logout)
        && await snackbar.RunAsync(() => api.SignOutUserAsync(id), l["The user was signed out everywhere."]);

    public static async Task<bool> DeleteAsync(IDialogService dialogs, ICoworkeeApi api, ISnackbar snackbar, CoworkeeLocalizer l, Guid id, string email) =>
        await dialogs.ConfirmAsync(l["Delete user"], l["Delete {0} and all personal data? This cannot be undone.", email], l["Delete"], l["Cancel"],
            Icons.Material.Outlined.DeleteForever)
        && await snackbar.RunAsync(() => api.DeleteUserAsync(id), l["User deleted"]);

    /// <summary>A part of the user page (e.g. <see cref="Components.Users.UserRolesTab"/>) in a side sheet; it saves by itself, so the sheet only closes.</summary>
    /// <param name="parameters">Sets the parameters on an instance MudEx reads them from; it renders a component of its own.</param>
    public static async Task ShowAsync<TPart>(IDialogService dialogs, string title, Action<TPart> parameters) where TPart : Microsoft.AspNetCore.Components.ComponentBase, new() =>
        await dialogs.ShowComponentInDialogAsync(title, string.Empty, parameters, await EditDialogExtensions.SideSheetAsync());

    private sealed class LockForm
    {
        /// <summary>Locked through this day; empty locks until an administrator unlocks.</summary>
        [Display(Name = "Locked through (empty: until unlocked)")]
        public DateTime? Until { get; set; }
    }
}

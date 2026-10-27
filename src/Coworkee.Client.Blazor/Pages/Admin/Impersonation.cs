using System.Security.Claims;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>"Log in as": a ticket of the API goes to the BFF, which has the auth server switch the sign-in and comes back as the user.</summary>
internal static class Impersonation
{
    /// <summary>Not for the own account and not while signed in as someone else already; the server checks the rest.</summary>
    public static bool Offers(ClaimsPrincipal user, Guid userId) =>
        Security.ClientUser.From(user).UserId != userId && !user.HasClaim(c => c.Type == ImpersonationClaims.Impersonator);

    public static async Task StartAsync(IDialogService dialogs, ICoworkeeApi api, ISnackbar snackbar, NavigationManager nav, CoworkeeLocalizer l, Guid userId, string name)
    {
        if (!await dialogs.ConfirmAsync(l["Log in as"], l["You see and do what {0} can until you go back to your account. The audit log names you as well.", name],
                l["Log in as"], l["Cancel"], Icons.Material.Outlined.SupervisorAccount))
        {
            return;
        }

        ImpersonationTicketDto? ticket = null;
        if (await snackbar.RunAsync(async () => ticket = await api.StartImpersonationAsync(userId)))
        {
            nav.NavigateTo($"bff/impersonate?ticket={Uri.EscapeDataString(ticket!.Ticket)}&returnUrl=%2F", forceLoad: true);
        }
    }
}

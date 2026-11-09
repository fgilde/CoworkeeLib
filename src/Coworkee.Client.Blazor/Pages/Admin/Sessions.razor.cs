using System.Globalization;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Security;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>The active sessions of the organisation's users (the ended ones on request), live, with the number each user has.</summary>
public partial class Sessions
{
    private static readonly string[] SearchFields = [nameof(UserSessionDto.Device), nameof(UserSessionDto.Browser), nameof(UserSessionDto.IpAddress), nameof(UserSessionDto.ClientId)];
    private IReadOnlyDictionary<Guid, string> _names = new Dictionary<Guid, string>();
    private IReadOnlyDictionary<Guid, int> _counts = new Dictionary<Guid, int>();
    private CoworkeeDataTable<UserSessionDto>? _table;
    private bool _ended;
    private bool _canManage;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ISessionsApi SessionsApi { get; set; } = null!;

    [Inject] private ThemeService ThemeService { get; set; } = null!;

    [Inject] private Navigation.CoworkeePages Links { get; set; } = null!;

    [Inject] private IAuthorizationService Authorization { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = null!;

    private string? Filter => _ended ? null : $"EndedAt eq null and ExpiresAt gt {DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)}";

    protected override async Task OnInitializedAsync() =>
        _canManage = (await Authorization.AuthorizeAsync((await AuthenticationState).User, PermissionPolicy.For(IdentityPermissions.Sessions.Manage))).Succeeded;

    private string Name(Guid userId) => _names.GetValueOrDefault(userId, userId.ToString());

    private async Task LoadUsersAsync(IReadOnlyList<UserSessionDto> sessions)
    {
        var ids = sessions.Select(s => s.UserId).Concat(sessions.Select(s => s.ImpersonatorId).OfType<Guid>()).Distinct().ToList();
        if (ids.Count > 0)
        {
            await Snackbar.RunAsync(async () =>
            {
                _names = await Api.GetUserNamesAsync(ids);
                _counts = await SessionsApi.GetCountsAsync(ids);
            });
        }
    }

    private async Task ShowEndedAsync(bool ended)
    {
        _ended = ended;
        await ReloadAsync();
    }

    private async Task EndAsync(UserSessionDto session)
    {
        if (await Dialogs.ConfirmAsync(L["End session"], L["Sign {0} out of {1}?", Name(session.UserId), ProfileSessions.Describe(session)], L["End"], L["Cancel"],
                Icons.Material.Outlined.Logout)
            && await Snackbar.RunAsync(() => SessionsApi.EndAsync(session.Id), L["Session ended"]))
        {
            await ReloadAsync();
        }
    }

    private async Task EndAllAsync(Guid userId)
    {
        if (await Dialogs.ConfirmAsync(L["End all sessions"], L["Sign {0} out everywhere?", Name(userId)], L["End"], L["Cancel"], Icons.Material.Outlined.Logout)
            && await Snackbar.RunAsync(() => SessionsApi.EndAllAsync(userId), L["Signed out"]))
        {
            await ReloadAsync();
        }
    }

    private async Task EditLimitAsync(Guid userId)
    {
        UserSessionsDto? current = null;
        if (await Snackbar.RunAsync(async () => current = await SessionsApi.GetOfUserAsync(userId)))
        {
            await SessionLimits.EditAsync(Dialogs, SessionsApi, L, userId, Name(userId), current!.MaxSessions);
        }
    }

    private Task ReloadAsync() => _table?.ReloadAsync() ?? Task.CompletedTask;
}

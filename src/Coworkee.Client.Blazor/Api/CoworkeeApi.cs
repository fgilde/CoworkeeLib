using Coworkee.Contracts.Auditing;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Mailing;
using Coworkee.Contracts.Notifications;
using Coworkee.Contracts.Settings;
using Coworkee.Contracts.Theming;
using Coworkee.Contracts;

namespace Coworkee.Client.Blazor.Api;

internal sealed class CoworkeeApi(HttpClient http) : ApiClientBase(http), ICoworkeeApi
{
    private const string Identity = "api/v1/identity";
    private const string Settings = "api/v1/settings";
    private const string Mail = "api/v1/mail";
    private const string Themes = "api/v1/themes";
    private const string Versions = "api/v1/versions";

    public Task<BffUserDto> GetUserAsync(CancellationToken cancellationToken = default) => GetAsync<BffUserDto>("bff/user", cancellationToken);

    public Task<BffLogoutDto> LogoutAsync(CancellationToken cancellationToken = default) => SendAsync<BffLogoutDto>(HttpMethod.Post, "bff/logout", null, cancellationToken);

    public Task<SetupStatusDto> GetSetupStatusAsync(CancellationToken cancellationToken = default) => GetAsync<SetupStatusDto>("api/v1/setup/status", cancellationToken);

    public async Task<IReadOnlyList<SetupCheckDto>> GetSetupChecksAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<SetupCheckDto[]>("api/v1/setup/checks", cancellationToken);

    public Task SendPasswordResetAsync(Guid userId, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"{Identity}/users/{userId}/password-reset", null, cancellationToken);

    public Task<ProfileDto> GetMyProfileAsync(CancellationToken cancellationToken = default) => GetAsync<ProfileDto>($"{Identity}/me", cancellationToken);

    public Task<ProfileDto> UpdateMyProfileAsync(UpdateProfileRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<ProfileDto>(HttpMethod.Put, $"{Identity}/me", request, cancellationToken);

    public Task<ProfileDto> SetMyAvatarAsync(string? dataUrl, CancellationToken cancellationToken = default) =>
        SendAsync<ProfileDto>(HttpMethod.Put, $"{Identity}/me/avatar", new SetAvatarRequest(dataUrl), cancellationToken);

    public async Task<string> ExportMyPersonalDataAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendContentAsync(HttpMethod.Get, $"{Identity}/me/personal-data", null, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public Task DeleteMyAccountAsync(string email, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"{Identity}/me/delete", new DeleteAccountRequest(email), cancellationToken);

    public Task DeleteUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"{Identity}/users/{userId}", null, cancellationToken);

    public async Task<IReadOnlyList<UserCardDto>> GetUserCardsAsync(IReadOnlyList<Guid> userIds, CancellationToken cancellationToken = default) =>
        await SendAsync<UserCardDto[]>(HttpMethod.Post, $"{Identity}/users/cards", new IdListRequest(userIds), cancellationToken);

    public Task<UserDetailDto> GetUserDetailAsync(Guid userId, CancellationToken cancellationToken = default) =>
        GetAsync<UserDetailDto>($"{Identity}/users/{userId}", cancellationToken);

    public Task UnlockUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"{Identity}/users/{userId}/unlock", null, cancellationToken);

    public Task LockUserAsync(Guid userId, DateTimeOffset? until, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"{Identity}/users/{userId}/lock", new LockUserRequest(until), cancellationToken);

    public Task SignOutUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"{Identity}/users/{userId}/sign-out", null, cancellationToken);

    public Task ChangeMyEmailAsync(ChangeEmailRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"{Identity}/me/email", request, cancellationToken);

    public Task SetUserEmailAsync(Guid userId, SetUserEmailRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Identity}/users/{userId}/email", request, cancellationToken);

    public Task SendInvitationAsync(Guid userId, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"{Identity}/users/{userId}/invitation", null, cancellationToken);

    public Task SetUserPasswordAsync(Guid userId, SetPasswordRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Identity}/users/{userId}/password", request, cancellationToken);

    public Task ResetUserTwoFactorAsync(Guid userId, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"{Identity}/users/{userId}/two-factor/reset", null, cancellationToken);

    public Task ResetUserPasskeysAsync(Guid userId, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"{Identity}/users/{userId}/passkeys/reset", null, cancellationToken);

    public Task RemoveUserLoginAsync(Guid userId, UserLoginDto login, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"{Identity}/users/{userId}/logins?provider={Uri.EscapeDataString(login.LoginProvider)}&key={Uri.EscapeDataString(login.ProviderKey)}", null, cancellationToken);

    public Task SetUserGroupsAsync(Guid userId, IReadOnlyList<Guid> groupIds, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Identity}/users/{userId}/groups", new IdListRequest(groupIds), cancellationToken);

    public Task SetUserAvatarAsync(Guid userId, string? dataUrl, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Identity}/users/{userId}/avatar", new SetAvatarRequest(dataUrl), cancellationToken);

    public Task<UserLanguageDto> GetUserLanguageAsync(Guid userId, CancellationToken cancellationToken = default) =>
        GetAsync<UserLanguageDto>($"{Identity}/users/{userId}/language", cancellationToken);

    public Task SetUserLanguageAsync(Guid userId, string? culture, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Identity}/users/{userId}/language", new UserLanguageDto(culture), cancellationToken);

    public async Task<IReadOnlyList<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await GetAsync<string[]>($"{Identity}/users/{userId}/permissions", cancellationToken);

    public Task<SetupResultDto> CompleteSetupAsync(CompleteSetupRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<SetupResultDto>(HttpMethod.Post, "api/v1/setup/complete", request, cancellationToken);

    public async Task<IReadOnlyCollection<string>> GetMyPermissionsAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<string[]>($"{Identity}/permissions/me", cancellationToken);

    public Task<MyRolesDto> GetMyRolesAsync(string? permission = null, CancellationToken cancellationToken = default) =>
        GetAsync<MyRolesDto>($"{Identity}/roles/me{(permission is null ? string.Empty : "?permission=" + Uri.EscapeDataString(permission))}", cancellationToken);

    public Task SetActiveRoleAsync(Guid? roleId, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Identity}/roles/me/active", new SetActiveRoleRequest(roleId), cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetOnlineUsersAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<Guid[]>(Contracts.Realtime.PresenceEvents.Path.TrimStart('/'), cancellationToken);

    public Task<PagedResult<UserDto>> GetUsersAsync(PageRequest page, CancellationToken cancellationToken = default) =>
        GetAsync<PagedResult<UserDto>>($"{Identity}/users?page={page.Page}&pageSize={page.PageSize}&search={Uri.EscapeDataString(page.Search ?? string.Empty)}", cancellationToken);

    public Task<UserDto> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<UserDto>(HttpMethod.Post, $"{Identity}/users", request, cancellationToken);

    public Task UpdateUserAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Identity}/users/{id}", request, cancellationToken);

    public Task SetUserRolesAsync(Guid id, IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Identity}/users/{id}/roles", new IdListRequest(roleIds), cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<RoleRefDto>>> GetUsersRolesAsync(IReadOnlyList<Guid> userIds, CancellationToken cancellationToken = default) =>
        await SendAsync<Dictionary<Guid, IReadOnlyList<RoleRefDto>>>(HttpMethod.Post, $"{Identity}/users/roles", new IdListRequest(userIds), cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> GetUserNamesAsync(IReadOnlyList<Guid> userIds, CancellationToken cancellationToken = default) =>
        await SendAsync<Dictionary<Guid, string>>(HttpMethod.Post, $"{Identity}/users/names", new IdListRequest(userIds), cancellationToken);

    public async Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<RoleDto[]>($"{Identity}/roles", cancellationToken);

    public Task<Guid> CreateRoleAsync(RoleRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<Guid>(HttpMethod.Post, $"{Identity}/roles", request, cancellationToken);

    public Task UpdateRoleAsync(Guid id, RoleRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Identity}/roles/{id}", request, cancellationToken);

    public Task DeleteRoleAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"{Identity}/roles/{id}", null, cancellationToken);

    public async Task<IReadOnlyList<PermissionGroupDto>> GetPermissionDefinitionsAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<PermissionGroupDto[]>($"{Identity}/permissions/definitions", cancellationToken);

    public async Task<IReadOnlyList<string>> GetGrantsAsync(PermissionProviderType providerType, Guid providerKey, CancellationToken cancellationToken = default) =>
        await GetAsync<string[]>($"{Identity}/permissions/grants/{providerType}/{providerKey}", cancellationToken);

    public Task SetGrantsAsync(PermissionProviderType providerType, Guid providerKey, IReadOnlyList<string> names, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Identity}/permissions/grants/{providerType}/{providerKey}", new NameListRequest(names), cancellationToken);

    public Task<PagedResult<GroupDto>> GetGroupsAsync(PageRequest page, CancellationToken cancellationToken = default) =>
        GetAsync<PagedResult<GroupDto>>($"{Identity}/groups?page={page.Page}&pageSize={page.PageSize}&search={Uri.EscapeDataString(page.Search ?? string.Empty)}", cancellationToken);

    public Task<Guid> CreateGroupAsync(GroupRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<Guid>(HttpMethod.Post, $"{Identity}/groups", request, cancellationToken);

    public Task UpdateGroupAsync(Guid id, GroupRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Identity}/groups/{id}", request, cancellationToken);

    public Task DeleteGroupAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"{Identity}/groups/{id}", null, cancellationToken);

    public Task SetGroupMembersAsync(Guid id, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Identity}/groups/{id}/members", new IdListRequest(userIds), cancellationToken);

    public Task SetGroupRolesAsync(Guid id, IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Identity}/groups/{id}/roles", new IdListRequest(roleIds), cancellationToken);

    public async Task<IReadOnlyList<ResourcePermissionDto>> GetResourcePermissionsAsync(string resourceType, Guid resourceId, CancellationToken cancellationToken = default) =>
        await GetAsync<ResourcePermissionDto[]>($"{Identity}/resource-permissions/{resourceType}/{resourceId}", cancellationToken);

    public Task<Guid> GrantResourcePermissionAsync(string resourceType, Guid resourceId, GrantResourcePermissionRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<Guid>(HttpMethod.Post, $"{Identity}/resource-permissions/{resourceType}/{resourceId}", request, cancellationToken);

    public Task RevokeResourcePermissionAsync(string resourceType, Guid resourceId, Guid id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"{Identity}/resource-permissions/{resourceType}/{resourceId}/{id}", null, cancellationToken);

    public async Task<IReadOnlyList<SettingGroupDto>> GetSettingDefinitionsAsync(bool userScope, CancellationToken cancellationToken = default) =>
        await GetAsync<SettingGroupDto[]>(userScope ? $"{Settings}/definitions/user" : $"{Settings}/definitions", cancellationToken);

    public async Task<IReadOnlyList<SettingValueDto>> GetSettingsAsync(SettingScope scope, CancellationToken cancellationToken = default) =>
        await GetAsync<SettingValueDto[]>($"{Settings}/{Scope(scope)}", cancellationToken);

    public Task SetSettingsAsync(SettingScope scope, IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Settings}/{Scope(scope)}", new SetSettingsRequest(values), cancellationToken);

    public async Task<IReadOnlyList<MailTemplateSummaryDto>> GetMailTemplatesAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<MailTemplateSummaryDto[]>($"{Mail}/templates", cancellationToken);

    public Task<MailTemplateDto> GetMailTemplateAsync(string name, string culture, CancellationToken cancellationToken = default) =>
        GetAsync<MailTemplateDto>(Template(name, culture), cancellationToken);

    public Task CreateMailTemplateAsync(CreateMailTemplateRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"{Mail}/templates", request, cancellationToken);

    public Task DeleteMailTemplateAsync(string name, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"{Mail}/templates/{Uri.EscapeDataString(name)}", null, cancellationToken);

    public Task SaveMailTemplateAsync(string name, string culture, SaveMailTemplateRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, Template(name, culture), request, cancellationToken);

    public Task<MailTemplateLanguageAddedDto> AddMailTemplateLanguageAsync(string name, string culture, AddMailTemplateLanguageRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<MailTemplateLanguageAddedDto>(HttpMethod.Post, Template(name, culture), request, cancellationToken);

    public Task ResetMailTemplateAsync(string name, string culture, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, Template(name, culture), null, cancellationToken);

    public Task<RenderedMailDto> PreviewMailTemplateAsync(string name, string culture, SaveMailTemplateRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<RenderedMailDto>(HttpMethod.Post, Template(name, culture) + "/preview", request, cancellationToken);

    public Task SendTestMailAsync(string name, string culture, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, Template(name, culture) + "/test", null, cancellationToken);

    public async Task<IReadOnlyList<MailTemplateVersionDto>> GetMailTemplateHistoryAsync(string name, string culture, CancellationToken cancellationToken = default) =>
        await GetAsync<MailTemplateVersionDto[]>(Template(name, culture) + "/history", cancellationToken);

    public Task<PagedResult<OutgoingMailDto>> GetOutgoingMailsAsync(PageRequest page, OutgoingMailStatus? status, CancellationToken cancellationToken = default) =>
        GetAsync<PagedResult<OutgoingMailDto>>(
            $"{Mail}/outgoing?page={page.Page}&pageSize={page.PageSize}&search={Uri.EscapeDataString(page.Search ?? string.Empty)}{(status is null ? string.Empty : "&status=" + status)}",
            cancellationToken);

    public Task<RecipientPreviewDto> PreviewRecipientsAsync(RecipientSourceDto source, CancellationToken cancellationToken = default) =>
        SendAsync<RecipientPreviewDto>(HttpMethod.Post, $"{Mail}/recipients/preview", source, cancellationToken);

    public async Task<IReadOnlyList<RecipientListDto>> GetRecipientListsAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<RecipientListDto[]>($"{Mail}/recipient-lists", cancellationToken);

    public Task<RecipientListDto> SaveRecipientListAsync(SaveRecipientListRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<RecipientListDto>(HttpMethod.Post, $"{Mail}/recipient-lists", request, cancellationToken);

    public Task UpdateRecipientListAsync(Guid id, SaveRecipientListRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Mail}/recipient-lists/{id}", request, cancellationToken);

    public Task DeleteRecipientListAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"{Mail}/recipient-lists/{id}", null, cancellationToken);

    public async Task<IReadOnlyList<MailCampaignDto>> GetMailCampaignsAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<MailCampaignDto[]>($"{Mail}/campaigns", cancellationToken);

    public Task<MailCampaignDto> GetMailCampaignAsync(Guid id, CancellationToken cancellationToken = default) =>
        GetAsync<MailCampaignDto>($"{Mail}/campaigns/{id}", cancellationToken);

    public Task<MailCampaignDto> SaveMailCampaignAsync(Guid? id, SaveMailCampaignRequest request, CancellationToken cancellationToken = default) =>
        id is { } existing
            ? SendAsync<MailCampaignDto>(HttpMethod.Put, $"{Mail}/campaigns/{existing}", request, cancellationToken)
            : SendAsync<MailCampaignDto>(HttpMethod.Post, $"{Mail}/campaigns", request, cancellationToken);

    public Task DeleteMailCampaignAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"{Mail}/campaigns/{id}", null, cancellationToken);

    public Task SendMailCampaignTestAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"{Mail}/campaigns/{id}/test", null, cancellationToken);

    public Task<MailCampaignDto> ScheduleMailCampaignAsync(Guid id, DateTimeOffset? at, CancellationToken cancellationToken = default) =>
        SendAsync<MailCampaignDto>(HttpMethod.Post, $"{Mail}/campaigns/{id}/schedule", new ScheduleMailCampaignRequest(at), cancellationToken);

    public Task CancelMailCampaignAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"{Mail}/campaigns/{id}/cancel", null, cancellationToken);

    public async Task<IReadOnlyDictionary<string, string?>> GetClientSettingsAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<Dictionary<string, string?>>($"{Settings}/client", cancellationToken);

    public Task<ThemeDto> GetCurrentThemeAsync(CancellationToken cancellationToken = default) => GetAsync<ThemeDto>($"{Themes}/current", cancellationToken);

    public async Task<IReadOnlyList<ThemeDto>> GetThemesAsync(CancellationToken cancellationToken = default) => await GetAsync<ThemeDto[]>(Themes, cancellationToken);

    public async Task<IReadOnlyList<AppConfigurationDto>> GetAppConfigurationsAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<AppConfigurationDto[]>("api/v1/configuration", cancellationToken);

    public Task<AppConfigurationValuesDto> GetAppConfigurationAsync(string section, CancellationToken cancellationToken = default) =>
        GetAsync<AppConfigurationValuesDto>($"api/v1/configuration/{Uri.EscapeDataString(section)}", cancellationToken);

    public Task<AppConfigurationValuesDto> SaveAppConfigurationAsync(string section, System.Text.Json.JsonElement values, CancellationToken cancellationToken = default) =>
        SendAsync<AppConfigurationValuesDto>(HttpMethod.Put, $"api/v1/configuration/{Uri.EscapeDataString(section)}", values, cancellationToken);

    public Task<AppConfigurationValuesDto> ResetAppConfigurationAsync(string section, CancellationToken cancellationToken = default) =>
        SendAsync<AppConfigurationValuesDto>(HttpMethod.Delete, $"api/v1/configuration/{Uri.EscapeDataString(section)}", null, cancellationToken);

    public async Task<ServiceDirectoryResult> GetServicesAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<ServiceDirectoryResult>("api/v1/services", cancellationToken);

    public async Task<SystemStatusDto> GetSystemStatusAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<SystemStatusDto>("api/v1/system/status", cancellationToken);

    public async Task<IReadOnlyList<ThemeDto>> GetBuiltInThemesAsync(CancellationToken cancellationToken = default) => await GetAsync<ThemeDto[]>($"{Themes}/built-in", cancellationToken);

    public Task<ThemeDto> CreateThemeAsync(ThemeRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<ThemeDto>(HttpMethod.Post, Themes, request, cancellationToken);

    public Task<ThemeDto> UpdateThemeAsync(Guid id, ThemeRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<ThemeDto>(HttpMethod.Put, $"{Themes}/{id}", request, cancellationToken);

    public Task DeleteThemeAsync(Guid id, CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Delete, $"{Themes}/{id}", null, cancellationToken);

    public Task SetDefaultThemeAsync(Guid id, CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Post, $"{Themes}/{id}/default", null, cancellationToken);

    public Task<KeycloakThemeStatusDto> GetKeycloakThemeStatusAsync(CancellationToken cancellationToken = default) =>
        GetAsync<KeycloakThemeStatusDto>($"{Themes}/keycloak", cancellationToken);

    public Task<KeycloakThemeStatusDto> ApplyKeycloakThemeAsync(CancellationToken cancellationToken = default) =>
        SendAsync<KeycloakThemeStatusDto>(HttpMethod.Post, $"{Themes}/keycloak/apply", null, cancellationToken);

    public Task<PagedResult<AuditEntryDto>> GetAuditAsync(AuditQuery query, CancellationToken cancellationToken = default)
    {
        var parameters = new List<string> { $"page={query.Page}", $"pageSize={query.PageSize}" };
        Add(parameters, "entityType", query.EntityType);
        Add(parameters, "entityId", query.EntityId);
        Add(parameters, "actorId", query.ActorId?.ToString());
        Add(parameters, "from", query.From?.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
        Add(parameters, "to", query.To?.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
        return GetAsync<PagedResult<AuditEntryDto>>($"api/v1/audit?{string.Join('&', parameters)}", cancellationToken);
    }

    public async Task<IReadOnlyList<EntityVersionDto>> GetVersionsAsync(string type, Guid id, CancellationToken cancellationToken = default) =>
        await GetAsync<EntityVersionDto[]>($"{Versions}/{Uri.EscapeDataString(type)}/{id}", cancellationToken);

    public Task<EntityVersionDetailDto> GetVersionAsync(string type, Guid id, int revision, CancellationToken cancellationToken = default) =>
        GetAsync<EntityVersionDetailDto>($"{Versions}/{Uri.EscapeDataString(type)}/{id}/{revision}", cancellationToken);

    public Task RestoreVersionAsync(string type, Guid id, int revision, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"{Versions}/{Uri.EscapeDataString(type)}/{id}/{revision}/restore", null, cancellationToken);

    public Task<UnreadCountDto> GetUnreadNotificationCountAsync(CancellationToken cancellationToken = default) =>
        GetAsync<UnreadCountDto>("api/v1/notifications/unread-count", cancellationToken);

    public Task<PagedResult<NotificationDto>> GetNotificationsAsync(bool unreadOnly, PageRequest page, CancellationToken cancellationToken = default) =>
        GetAsync<PagedResult<NotificationDto>>($"api/v1/notifications?unreadOnly={unreadOnly.ToString().ToLowerInvariant()}&page={page.Page}&pageSize={page.PageSize}", cancellationToken);

    public Task MarkNotificationReadAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"api/v1/notifications/{id}/read", null, cancellationToken);

    public Task MarkAllNotificationsReadAsync(CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, "api/v1/notifications/read-all", null, cancellationToken);

    public Task MarkNotificationUnreadAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"api/v1/notifications/{id}/unread", null, cancellationToken);

    public Task DeleteNotificationAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"api/v1/notifications/{id}", null, cancellationToken);

    public Task DeleteAllNotificationsAsync(CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, "api/v1/notifications", null, cancellationToken);

    public Task<PushSettingsDto> GetPushSettingsAsync(CancellationToken cancellationToken = default) =>
        GetAsync<PushSettingsDto>("api/v1/notifications/push", cancellationToken);

    public Task<PushDeviceDto> SavePushSubscriptionAsync(PushSubscriptionRequest subscription, CancellationToken cancellationToken = default) =>
        SendAsync<PushDeviceDto>(HttpMethod.Put, "api/v1/notifications/push", subscription, cancellationToken);

    public Task DeletePushSubscriptionAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"api/v1/notifications/push/{id}", null, cancellationToken);

    private static void Add(List<string> parameters, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parameters.Add($"{name}={Uri.EscapeDataString(value)}");
        }
    }

    private static string Scope(SettingScope scope) => scope.ToString().ToLowerInvariant();

    private static string Template(string name, string culture) => $"{Mail}/templates/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(culture)}";
}

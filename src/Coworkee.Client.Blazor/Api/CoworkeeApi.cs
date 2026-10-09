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

    public async Task<IReadOnlyList<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await GetAsync<string[]>($"{Identity}/users/{userId}/permissions", cancellationToken);

    public Task<SetupResultDto> CompleteSetupAsync(CompleteSetupRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<SetupResultDto>(HttpMethod.Post, "api/v1/setup/complete", request, cancellationToken);

    public async Task<IReadOnlyCollection<string>> GetMyPermissionsAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<string[]>($"{Identity}/permissions/me", cancellationToken);

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

    public Task SaveMailTemplateAsync(string name, string culture, SaveMailTemplateRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, Template(name, culture), request, cancellationToken);

    public Task ResetMailTemplateAsync(string name, string culture, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, Template(name, culture), null, cancellationToken);

    public Task<RenderedMailDto> PreviewMailTemplateAsync(string name, string culture, SaveMailTemplateRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<RenderedMailDto>(HttpMethod.Post, Template(name, culture) + "/preview", request, cancellationToken);

    public Task SendTestMailAsync(string name, string culture, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, Template(name, culture) + "/test", null, cancellationToken);

    public Task<PagedResult<OutgoingMailDto>> GetOutgoingMailsAsync(PageRequest page, OutgoingMailStatus? status, CancellationToken cancellationToken = default) =>
        GetAsync<PagedResult<OutgoingMailDto>>(
            $"{Mail}/outgoing?page={page.Page}&pageSize={page.PageSize}&search={Uri.EscapeDataString(page.Search ?? string.Empty)}{(status is null ? string.Empty : "&status=" + status)}",
            cancellationToken);

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

    public async Task<IReadOnlyList<ServiceDto>> GetServicesAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<ServiceDto[]>("api/v1/services", cancellationToken);

    public async Task<IReadOnlyList<ThemeDto>> GetBuiltInThemesAsync(CancellationToken cancellationToken = default) => await GetAsync<ThemeDto[]>($"{Themes}/built-in", cancellationToken);

    public Task<ThemeDto> CreateThemeAsync(ThemeRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<ThemeDto>(HttpMethod.Post, Themes, request, cancellationToken);

    public Task<ThemeDto> UpdateThemeAsync(Guid id, ThemeRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<ThemeDto>(HttpMethod.Put, $"{Themes}/{id}", request, cancellationToken);

    public Task DeleteThemeAsync(Guid id, CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Delete, $"{Themes}/{id}", null, cancellationToken);

    public Task SetDefaultThemeAsync(Guid id, CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Post, $"{Themes}/{id}/default", null, cancellationToken);

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

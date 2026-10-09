using Coworkee.Contracts.Auditing;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Mailing;
using Coworkee.Contracts.Notifications;
using Coworkee.Contracts.Settings;
using Coworkee.Contracts.Theming;
using Coworkee.Contracts;

namespace Coworkee.Client.Blazor.Api;

public interface ICoworkeeApi
{
    Task<BffUserDto> GetUserAsync(CancellationToken cancellationToken = default);

    Task<BffLogoutDto> LogoutAsync(CancellationToken cancellationToken = default);

    Task<SetupStatusDto> GetSetupStatusAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SetupCheckDto>> GetSetupChecksAsync(CancellationToken cancellationToken = default);

    Task SendPasswordResetAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<ProfileDto> GetMyProfileAsync(CancellationToken cancellationToken = default);

    Task<ProfileDto> UpdateMyProfileAsync(UpdateProfileRequest request, CancellationToken cancellationToken = default);

    /// <summary>Sets the own profile picture (data URL) or removes it with null.</summary>
    Task<ProfileDto> SetMyAvatarAsync(string? dataUrl, CancellationToken cancellationToken = default);

    /// <summary>The own personal data as JSON, one section per module.</summary>
    Task<string> ExportMyPersonalDataAsync(CancellationToken cancellationToken = default);

    Task DeleteMyAccountAsync(string email, CancellationToken cancellationToken = default);

    Task DeleteUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserCardDto>> GetUserCardsAsync(IReadOnlyList<Guid> userIds, CancellationToken cancellationToken = default);

    Task<UserDetailDto> GetUserDetailAsync(Guid userId, CancellationToken cancellationToken = default);

    Task UnlockUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Locks the user out until <paramref name="until"/> or for good, and ends the user's sessions at once.</summary>
    Task LockUserAsync(Guid userId, DateTimeOffset? until, CancellationToken cancellationToken = default);

    /// <summary>Ends all sessions of the user at once; the open clients of the user sign out.</summary>
    Task SignOutUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<SetupResultDto> CompleteSetupAsync(CompleteSetupRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<string>> GetMyPermissionsAsync(CancellationToken cancellationToken = default);

    Task<PagedResult<UserDto>> GetUsersAsync(PageRequest page, CancellationToken cancellationToken = default);

    Task<UserDto> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default);

    Task UpdateUserAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default);

    Task SetUserRolesAsync(Guid id, IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, IReadOnlyList<RoleRefDto>>> GetUsersRolesAsync(IReadOnlyList<Guid> userIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, string>> GetUserNamesAsync(IReadOnlyList<Guid> userIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken cancellationToken = default);

    Task<Guid> CreateRoleAsync(RoleRequest request, CancellationToken cancellationToken = default);

    Task UpdateRoleAsync(Guid id, RoleRequest request, CancellationToken cancellationToken = default);

    Task DeleteRoleAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PermissionGroupDto>> GetPermissionDefinitionsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetGrantsAsync(PermissionProviderType providerType, Guid providerKey, CancellationToken cancellationToken = default);

    Task SetGrantsAsync(PermissionProviderType providerType, Guid providerKey, IReadOnlyList<string> names, CancellationToken cancellationToken = default);

    Task<PagedResult<GroupDto>> GetGroupsAsync(PageRequest page, CancellationToken cancellationToken = default);

    Task<Guid> CreateGroupAsync(GroupRequest request, CancellationToken cancellationToken = default);

    Task UpdateGroupAsync(Guid id, GroupRequest request, CancellationToken cancellationToken = default);

    Task DeleteGroupAsync(Guid id, CancellationToken cancellationToken = default);

    Task SetGroupMembersAsync(Guid id, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken = default);

    Task SetGroupRolesAsync(Guid id, IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResourcePermissionDto>> GetResourcePermissionsAsync(string resourceType, Guid resourceId, CancellationToken cancellationToken = default);

    Task<Guid> GrantResourcePermissionAsync(string resourceType, Guid resourceId, GrantResourcePermissionRequest request, CancellationToken cancellationToken = default);

    Task RevokeResourcePermissionAsync(string resourceType, Guid resourceId, Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SettingGroupDto>> GetSettingDefinitionsAsync(bool userScope, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SettingValueDto>> GetSettingsAsync(SettingScope scope, CancellationToken cancellationToken = default);

    Task SetSettingsAsync(SettingScope scope, IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MailTemplateSummaryDto>> GetMailTemplatesAsync(CancellationToken cancellationToken = default);

    Task<MailTemplateDto> GetMailTemplateAsync(string name, string culture, CancellationToken cancellationToken = default);

    Task SaveMailTemplateAsync(string name, string culture, SaveMailTemplateRequest request, CancellationToken cancellationToken = default);

    Task ResetMailTemplateAsync(string name, string culture, CancellationToken cancellationToken = default);

    Task<RenderedMailDto> PreviewMailTemplateAsync(string name, string culture, SaveMailTemplateRequest request, CancellationToken cancellationToken = default);

    Task SendTestMailAsync(string name, string culture, CancellationToken cancellationToken = default);

    Task<PagedResult<OutgoingMailDto>> GetOutgoingMailsAsync(PageRequest page, OutgoingMailStatus? status, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, string?>> GetClientSettingsAsync(CancellationToken cancellationToken = default);

    Task<ThemeDto> GetCurrentThemeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ThemeDto>> GetThemesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AppConfigurationDto>> GetAppConfigurationsAsync(CancellationToken cancellationToken = default);

    Task<AppConfigurationValuesDto> GetAppConfigurationAsync(string section, CancellationToken cancellationToken = default);

    Task<AppConfigurationValuesDto> SaveAppConfigurationAsync(string section, System.Text.Json.JsonElement values, CancellationToken cancellationToken = default);

    Task<AppConfigurationValuesDto> ResetAppConfigurationAsync(string section, CancellationToken cancellationToken = default);

    /// <summary>The themes that ship with the app; answers before setup.</summary>
    Task<IReadOnlyList<ThemeDto>> GetBuiltInThemesAsync(CancellationToken cancellationToken = default);

    Task<ThemeDto> CreateThemeAsync(ThemeRequest request, CancellationToken cancellationToken = default);

    Task<ThemeDto> UpdateThemeAsync(Guid id, ThemeRequest request, CancellationToken cancellationToken = default);

    Task DeleteThemeAsync(Guid id, CancellationToken cancellationToken = default);

    Task SetDefaultThemeAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PagedResult<AuditEntryDto>> GetAuditAsync(AuditQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EntityVersionDto>> GetVersionsAsync(string type, Guid id, CancellationToken cancellationToken = default);

    Task<EntityVersionDetailDto> GetVersionAsync(string type, Guid id, int revision, CancellationToken cancellationToken = default);

    Task RestoreVersionAsync(string type, Guid id, int revision, CancellationToken cancellationToken = default);

    Task<UnreadCountDto> GetUnreadNotificationCountAsync(CancellationToken cancellationToken = default);

    Task<PagedResult<NotificationDto>> GetNotificationsAsync(bool unreadOnly, PageRequest page, CancellationToken cancellationToken = default);

    Task MarkNotificationReadAsync(Guid id, CancellationToken cancellationToken = default);

    Task MarkAllNotificationsReadAsync(CancellationToken cancellationToken = default);
}

public sealed class ApiException(int status, string? code, IReadOnlyDictionary<string, string[]>? errors)
    : Exception(code ?? $"Request failed with status {status}.")
{
    public int Status { get; } = status;

    public string? Code { get; } = code;

    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;
}

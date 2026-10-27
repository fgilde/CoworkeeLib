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

    /// <summary>A ticket to sign in as the user ("Log in as"); <c>bff/impersonate?ticket=...</c> redeems it within a minute.</summary>
    Task<ImpersonationTicketDto> StartImpersonationAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Mails a confirmation link to the new address; the current address stays until the link is opened.</summary>
    Task ChangeMyEmailAsync(ChangeEmailRequest request, CancellationToken cancellationToken = default);

    Task SetUserEmailAsync(Guid userId, SetUserEmailRequest request, CancellationToken cancellationToken = default);

    /// <summary>Invites the user by mail to choose a password.</summary>
    Task SendInvitationAsync(Guid userId, CancellationToken cancellationToken = default);

    Task SetUserPasswordAsync(Guid userId, SetPasswordRequest request, CancellationToken cancellationToken = default);

    Task ResetUserTwoFactorAsync(Guid userId, CancellationToken cancellationToken = default);

    Task ResetUserPasskeysAsync(Guid userId, CancellationToken cancellationToken = default);

    Task RemoveUserLoginAsync(Guid userId, UserLoginDto login, CancellationToken cancellationToken = default);

    Task SetUserGroupsAsync(Guid userId, IReadOnlyList<Guid> groupIds, CancellationToken cancellationToken = default);

    Task SetUserAvatarAsync(Guid userId, string? dataUrl, CancellationToken cancellationToken = default);

    Task<UserLanguageDto> GetUserLanguageAsync(Guid userId, CancellationToken cancellationToken = default);

    Task SetUserLanguageAsync(Guid userId, string? culture, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<SetupResultDto> CompleteSetupAsync(CompleteSetupRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<string>> GetMyPermissionsAsync(CancellationToken cancellationToken = default);

    /// <summary>The own roles; with <paramref name="permission"/> each says whether it grants it.</summary>
    Task<MyRolesDto> GetMyRolesAsync(string? permission = null, CancellationToken cancellationToken = default);

    Task SetActiveRoleAsync(Guid? roleId, CancellationToken cancellationToken = default);

    /// <summary>The people of the own organisation with an open connection.</summary>
    Task<IReadOnlyList<Guid>> GetOnlineUsersAsync(CancellationToken cancellationToken = default);

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

    /// <summary>The direct children of a group ordered by name, the roots for <c>null</c>.</summary>
    Task<IReadOnlyList<GroupNodeDto>> GetGroupChildrenAsync(Guid? parentId, CancellationToken cancellationToken = default);

    /// <summary>Groups whose name contains the text, with their ancestors, in tree order.</summary>
    Task<IReadOnlyList<GroupNodeDto>> SearchGroupTreeAsync(string search, CancellationToken cancellationToken = default);

    /// <summary>Moves a group with its subtree under another parent, to the roots for <c>null</c>.</summary>
    Task MoveGroupAsync(Guid id, Guid? parentId, CancellationToken cancellationToken = default);

    Task AddGroupMembersAsync(Guid id, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken = default);

    Task RemoveGroupMembersAsync(Guid id, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken = default);

    /// <summary>Moves all members of a group into another group.</summary>
    Task MoveGroupMembersAsync(Guid from, Guid to, CancellationToken cancellationToken = default);

    /// <summary>The groups the signed-in user may work in, with membership and selection.</summary>
    Task<IReadOnlyList<MyGroupDto>> GetMyGroupsAsync(CancellationToken cancellationToken = default);

    /// <summary>The groups listings narrow to; empty for all accessible ones.</summary>
    Task SelectGroupsAsync(IReadOnlyList<Guid> groupIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResourcePermissionDto>> GetResourcePermissionsAsync(string resourceType, Guid resourceId, CancellationToken cancellationToken = default);

    Task<Guid> GrantResourcePermissionAsync(string resourceType, Guid resourceId, GrantResourcePermissionRequest request, CancellationToken cancellationToken = default);

    Task RevokeResourcePermissionAsync(string resourceType, Guid resourceId, Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SettingGroupDto>> GetSettingDefinitionsAsync(bool userScope, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SettingValueDto>> GetSettingsAsync(SettingScope scope, CancellationToken cancellationToken = default);

    Task SetSettingsAsync(SettingScope scope, IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MailTemplateSummaryDto>> GetMailTemplatesAsync(CancellationToken cancellationToken = default);

    Task<MailTemplateDto> GetMailTemplateAsync(string name, string culture, CancellationToken cancellationToken = default);

    Task CreateMailTemplateAsync(CreateMailTemplateRequest request, CancellationToken cancellationToken = default);

    Task DeleteMailTemplateAsync(string name, CancellationToken cancellationToken = default);

    Task SaveMailTemplateAsync(string name, string culture, SaveMailTemplateRequest request, CancellationToken cancellationToken = default);

    Task<MailTemplateLanguageAddedDto> AddMailTemplateLanguageAsync(string name, string culture, AddMailTemplateLanguageRequest request, CancellationToken cancellationToken = default);

    Task ResetMailTemplateAsync(string name, string culture, CancellationToken cancellationToken = default);

    Task<RenderedMailDto> PreviewMailTemplateAsync(string name, string culture, SaveMailTemplateRequest request, CancellationToken cancellationToken = default);

    Task SendTestMailAsync(string name, string culture, CancellationToken cancellationToken = default);

    /// <summary>The saved states of a template language, newest first.</summary>
    Task<IReadOnlyList<MailTemplateVersionDto>> GetMailTemplateHistoryAsync(string name, string culture, CancellationToken cancellationToken = default);

    Task<PagedResult<OutgoingMailDto>> GetOutgoingMailsAsync(PageRequest page, OutgoingMailStatus? status, CancellationToken cancellationToken = default);

    Task<RecipientPreviewDto> PreviewRecipientsAsync(RecipientSourceDto source, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecipientListDto>> GetRecipientListsAsync(CancellationToken cancellationToken = default);

    Task<RecipientListDto> SaveRecipientListAsync(SaveRecipientListRequest request, CancellationToken cancellationToken = default);

    Task UpdateRecipientListAsync(Guid id, SaveRecipientListRequest request, CancellationToken cancellationToken = default);

    Task DeleteRecipientListAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MailCampaignDto>> GetMailCampaignsAsync(CancellationToken cancellationToken = default);

    Task<MailCampaignDto> GetMailCampaignAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Creates the campaign, or changes a draft when <paramref name="id"/> is set.</summary>
    Task<MailCampaignDto> SaveMailCampaignAsync(Guid? id, SaveMailCampaignRequest request, CancellationToken cancellationToken = default);

    Task DeleteMailCampaignAsync(Guid id, CancellationToken cancellationToken = default);

    Task SendMailCampaignTestAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Starts the campaign at <paramref name="at"/>, now when <c>null</c>.</summary>
    Task<MailCampaignDto> ScheduleMailCampaignAsync(Guid id, DateTimeOffset? at, CancellationToken cancellationToken = default);

    Task CancelMailCampaignAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, string?>> GetClientSettingsAsync(CancellationToken cancellationToken = default);

    Task<ThemeDto> GetCurrentThemeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ThemeDto>> GetThemesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AppConfigurationDto>> GetAppConfigurationsAsync(CancellationToken cancellationToken = default);

    Task<AppConfigurationValuesDto> GetAppConfigurationAsync(string section, CancellationToken cancellationToken = default);

    Task<AppConfigurationValuesDto> SaveAppConfigurationAsync(string section, System.Text.Json.JsonElement values, CancellationToken cancellationToken = default);

    Task<AppConfigurationValuesDto> ResetAppConfigurationAsync(string section, CancellationToken cancellationToken = default);

    /// <summary>The services of the app (Coworkee:Services) with the status the server sees.</summary>
    Task<ServiceDirectoryResult> GetServicesAsync(CancellationToken cancellationToken = default);

    /// <summary>The status of the installation (system organisation only): versions, database, jobs, mail and what the modules add.</summary>
    Task<SystemStatusDto> GetSystemStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>The themes that ship with the app; answers before setup.</summary>
    Task<IReadOnlyList<ThemeDto>> GetBuiltInThemesAsync(CancellationToken cancellationToken = default);

    Task<ThemeDto> CreateThemeAsync(ThemeRequest request, CancellationToken cancellationToken = default);

    Task<ThemeDto> UpdateThemeAsync(Guid id, ThemeRequest request, CancellationToken cancellationToken = default);

    Task DeleteThemeAsync(Guid id, CancellationToken cancellationToken = default);

    Task SetDefaultThemeAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The Keycloak login theme the API keeps in the app's design.</summary>
    Task<KeycloakThemeStatusDto> GetKeycloakThemeStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes the Keycloak login theme now.</summary>
    Task<KeycloakThemeStatusDto> ApplyKeycloakThemeAsync(CancellationToken cancellationToken = default);

    Task<PagedResult<AuditEntryDto>> GetAuditAsync(AuditQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EntityVersionDto>> GetVersionsAsync(string type, Guid id, CancellationToken cancellationToken = default);

    Task<EntityVersionDetailDto> GetVersionAsync(string type, Guid id, int revision, CancellationToken cancellationToken = default);

    Task RestoreVersionAsync(string type, Guid id, int revision, CancellationToken cancellationToken = default);

    Task<UnreadCountDto> GetUnreadNotificationCountAsync(CancellationToken cancellationToken = default);

    Task<PagedResult<NotificationDto>> GetNotificationsAsync(bool unreadOnly, PageRequest page, CancellationToken cancellationToken = default);

    /// <summary>The inbox filtered by category, open or done, unread and text (<see cref="NotificationFilter.Search"/>).</summary>
    Task<PagedResult<NotificationDto>> GetNotificationsAsync(NotificationFilter filter, PageRequest page, CancellationToken cancellationToken = default);

    Task<NotificationCountsDto> GetNotificationCountsAsync(CancellationToken cancellationToken = default);

    /// <summary>Done also marks read; not done brings it back into the open inbox.</summary>
    Task MarkNotificationDoneAsync(Guid id, bool done, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationPreferenceDto>> GetNotificationPreferencesAsync(CancellationToken cancellationToken = default);

    Task<NotificationPreferenceDto> SaveNotificationPreferenceAsync(string type, SaveNotificationPreferenceRequest preference, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FollowDto>> GetFollowsAsync(CancellationToken cancellationToken = default);

    Task<FollowStateDto> GetFollowStateAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default);

    Task<FollowStateDto> FollowAsync(string entityType, Guid entityId, string? title = null, CancellationToken cancellationToken = default);

    Task<FollowStateDto> UnfollowAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default);

    Task MarkNotificationReadAsync(Guid id, CancellationToken cancellationToken = default);

    Task MarkAllNotificationsReadAsync(CancellationToken cancellationToken = default);

    Task MarkNotificationUnreadAsync(Guid id, CancellationToken cancellationToken = default);

    Task DeleteNotificationAsync(Guid id, CancellationToken cancellationToken = default);

    Task DeleteAllNotificationsAsync(CancellationToken cancellationToken = default);

    Task<PushSettingsDto> GetPushSettingsAsync(CancellationToken cancellationToken = default);

    Task<PushDeviceDto> SavePushSubscriptionAsync(PushSubscriptionRequest subscription, CancellationToken cancellationToken = default);

    Task DeletePushSubscriptionAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class ApiException(int status, string? code, IReadOnlyDictionary<string, string[]>? errors)
    : Exception(code ?? $"Request failed with status {status}.")
{
    public int Status { get; } = status;

    public string? Code { get; } = code;

    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;
}

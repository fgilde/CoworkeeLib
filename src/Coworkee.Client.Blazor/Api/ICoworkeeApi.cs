using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Mailing;
using Coworkee.Contracts.Settings;

namespace Coworkee.Client.Blazor.Api;

public interface ICoworkeeApi
{
    Task<BffUserDto> GetUserAsync(CancellationToken cancellationToken = default);

    Task<BffLogoutDto> LogoutAsync(CancellationToken cancellationToken = default);

    Task<SetupStatusDto> GetSetupStatusAsync(CancellationToken cancellationToken = default);

    Task<SetupResultDto> CompleteSetupAsync(CompleteSetupRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<string>> GetMyPermissionsAsync(CancellationToken cancellationToken = default);

    Task<PagedResult<UserDto>> GetUsersAsync(PageRequest page, CancellationToken cancellationToken = default);

    Task<UserDto> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default);

    Task UpdateUserAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default);

    Task SetUserRolesAsync(Guid id, IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken = default);

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
}

public sealed class ApiException(int status, string? code, IReadOnlyDictionary<string, string[]>? errors)
    : Exception(code ?? $"Request failed with status {status}.")
{
    public int Status { get; } = status;

    public string? Code { get; } = code;

    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;
}

using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Results;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Settings;

internal sealed class SettingDefinitionHandlers(ISettingDefinitionManager definitions, IPermissionChecker permissions)
    : IHandler<GetSettingDefinitions, Result<IReadOnlyList<SettingGroupDto>>>, IHandler<GetUserSettingDefinitions, Result<IReadOnlyList<SettingGroupDto>>>
{
    public Task<Result<IReadOnlyList<SettingGroupDto>>> HandleAsync(GetSettingDefinitions query, CancellationToken cancellationToken) =>
        GroupsAsync(_ => true, cancellationToken);

    public Task<Result<IReadOnlyList<SettingGroupDto>>> HandleAsync(GetUserSettingDefinitions query, CancellationToken cancellationToken) =>
        GroupsAsync(d => d.Scopes.Contains(SettingScope.User), cancellationToken);

    // a setting with its own permission (the head script) is left out for whoever lacks it: the form would show it, saving would answer 403
    private async Task<Result<IReadOnlyList<SettingGroupDto>>> GroupsAsync(Func<SettingDefinition, bool> filter, CancellationToken cancellationToken)
    {
        var denied = new HashSet<string>(StringComparer.Ordinal);
        foreach (var permission in definitions.All.Select(d => d.Permission).OfType<string>().Distinct())
        {
            if (!await permissions.IsGrantedAsync(permission, cancellationToken))
            {
                denied.Add(permission);
            }
        }

        return Groups(d => filter(d) && (d.Permission is null || !denied.Contains(d.Permission)));
    }

    private Result<IReadOnlyList<SettingGroupDto>> Groups(Func<SettingDefinition, bool> filter)
    {
        IReadOnlyList<SettingGroupDto> groups = definitions.Groups
            .Select(g => new SettingGroupDto(g.Name, g.DisplayName, definitions.All
                .Where(d => d.Group == g.Name && filter(d))
                .Select(d => new SettingDefinitionDto(d.Name, d.DisplayName, d.Description, d.Type, d.IsEncrypted ? null : d.DefaultValue, d.Scopes, d.Choices, d.EnabledWhen))
                .ToList(), g.Page))
            .Where(g => g.Settings.Count > 0)
            .ToList();
        return Result<IReadOnlyList<SettingGroupDto>>.Success(groups);
    }
}

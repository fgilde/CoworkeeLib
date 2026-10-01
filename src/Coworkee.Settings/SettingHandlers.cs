using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Settings;

[RequiresPermission(SettingsPermissions.Manage)]
public sealed record GetSettingDefinitions : IQuery<Result<IReadOnlyList<SettingGroupDto>>>;

public sealed record GetUserSettingDefinitions : IQuery<Result<IReadOnlyList<SettingGroupDto>>>;

[RequiresPermission(SettingsPermissions.Manage)]
public sealed record GetManagedSettings(SettingScope Scope) : IQuery<Result<IReadOnlyList<SettingValueDto>>>;

[RequiresPermission(SettingsPermissions.Manage)]
public sealed record SetManagedSettings(SettingScope Scope, IReadOnlyDictionary<string, string?> Values) : ICommand<Result>;

public sealed record GetUserSettings : IQuery<Result<IReadOnlyList<SettingValueDto>>>;

public sealed record SetUserSettings(IReadOnlyDictionary<string, string?> Values) : ICommand<Result>;

public sealed record GetClientSettings : IQuery<Result<IReadOnlyDictionary<string, string?>>>;

internal sealed class SettingDefinitionHandlers(ISettingDefinitionManager definitions)
    : IHandler<GetSettingDefinitions, Result<IReadOnlyList<SettingGroupDto>>>, IHandler<GetUserSettingDefinitions, Result<IReadOnlyList<SettingGroupDto>>>
{
    public Task<Result<IReadOnlyList<SettingGroupDto>>> HandleAsync(GetSettingDefinitions query, CancellationToken cancellationToken) =>
        Task.FromResult(Groups(_ => true));

    public Task<Result<IReadOnlyList<SettingGroupDto>>> HandleAsync(GetUserSettingDefinitions query, CancellationToken cancellationToken) =>
        Task.FromResult(Groups(d => d.Scopes.Contains(SettingScope.User)));

    private Result<IReadOnlyList<SettingGroupDto>> Groups(Func<SettingDefinition, bool> filter)
    {
        IReadOnlyList<SettingGroupDto> groups = definitions.Groups
            .Select(g => new SettingGroupDto(g.Name, g.DisplayName, definitions.All
                .Where(d => d.Group == g.Name && filter(d))
                .Select(d => new SettingDefinitionDto(d.Name, d.DisplayName, d.Description, d.Type, d.IsEncrypted ? null : d.DefaultValue, d.Scopes, d.Choices))
                .ToList()))
            .Where(g => g.Settings.Count > 0)
            .ToList();
        return Result<IReadOnlyList<SettingGroupDto>>.Success(groups);
    }
}

internal sealed class SettingValueHandlers(
    CoworkeeDbContext db, ICurrentUser currentUser, ISettingDefinitionManager definitions, SettingProtector protector, ISettingProvider provider, IServiceProvider services)
    : IHandler<GetManagedSettings, Result<IReadOnlyList<SettingValueDto>>>,
      IHandler<SetManagedSettings, Result>,
      IHandler<GetUserSettings, Result<IReadOnlyList<SettingValueDto>>>,
      IHandler<SetUserSettings, Result>,
      IHandler<GetClientSettings, Result<IReadOnlyDictionary<string, string?>>>
{
    public Task<Result<IReadOnlyList<SettingValueDto>>> HandleAsync(GetManagedSettings query, CancellationToken cancellationToken) =>
        ReadAsync(query.Scope, cancellationToken);

    public Task<Result> HandleAsync(SetManagedSettings command, CancellationToken cancellationToken) =>
        command.Scope == SettingScope.User
            ? Task.FromResult<Result>(Error.Validation(nameof(command.Scope), "Use the user settings endpoint for user values."))
            : WriteAsync(command.Scope, command.Values, cancellationToken);

    public Task<Result<IReadOnlyList<SettingValueDto>>> HandleAsync(GetUserSettings query, CancellationToken cancellationToken) =>
        ReadAsync(SettingScope.User, cancellationToken);

    public Task<Result> HandleAsync(SetUserSettings command, CancellationToken cancellationToken) =>
        WriteAsync(SettingScope.User, command.Values, cancellationToken);

    public async Task<Result<IReadOnlyDictionary<string, string?>>> HandleAsync(GetClientSettings query, CancellationToken cancellationToken) =>
        Result<IReadOnlyDictionary<string, string?>>.Success(await provider.GetClientValuesAsync(cancellationToken));

    private async Task<Result<IReadOnlyList<SettingValueDto>>> ReadAsync(SettingScope scope, CancellationToken cancellationToken)
    {
        if (!TryScopeKey(scope, out var key, out var error) || !await MayUseAsync(scope, cancellationToken))
        {
            return error ?? GlobalForbidden;
        }

        var stored = await db.Set<SettingValue>().AsNoTracking()
            .Where(s => s.Scope == scope && s.ScopeKey == key)
            .ToDictionaryAsync(s => s.Name, s => s.Value, StringComparer.Ordinal, cancellationToken);
        IReadOnlyList<SettingValueDto> values = definitions.All
            .Where(d => d.Scopes.Contains(scope))
            .Select(d => stored.TryGetValue(d.Name, out var value)
                ? new SettingValueDto(d.Name, d.IsEncrypted ? null : value, true)
                : new SettingValueDto(d.Name, null, false))
            .ToList();
        return Result<IReadOnlyList<SettingValueDto>>.Success(values);
    }

    private async Task<Result> WriteAsync(SettingScope scope, IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken)
    {
        if (!TryScopeKey(scope, out var key, out var error) || !await MayUseAsync(scope, cancellationToken))
        {
            return error ?? GlobalForbidden;
        }

        foreach (var (name, value) in values)
        {
            var definition = definitions.Find(name);
            if (definition is null || !definition.Scopes.Contains(scope))
            {
                return Error.Validation(name, $"Setting '{name}' is not defined for scope {scope}.");
            }

            if (value is not null && definition.Validate(value) is { } message)
            {
                return Error.Validation(name, message);
            }
        }

        var names = values.Keys.ToList();
        var existing = await db.Set<SettingValue>()
            .Where(s => s.Scope == scope && s.ScopeKey == key && names.Contains(s.Name))
            .ToDictionaryAsync(s => s.Name, StringComparer.Ordinal, cancellationToken);
        foreach (var (name, value) in values)
        {
            var stored = value is not null && definitions.Find(name)!.IsEncrypted ? protector.Protect(value) : value;
            if (existing.TryGetValue(name, out var row))
            {
                if (stored is null)
                {
                    db.Remove(row);
                }
                else
                {
                    row.Value = stored;
                }
            }
            else if (stored is not null)
            {
                db.Add(new SettingValue { Name = name, Scope = scope, ScopeKey = key, Value = stored });
            }
        }

        return Result.Success();
    }

    private static readonly Error GlobalForbidden =
        Error.Forbidden("settings.global_forbidden", "System wide settings can only be changed from the system organisation.");

    private async Task<bool> MayUseAsync(SettingScope scope, CancellationToken cancellationToken) =>
        scope != SettingScope.Global
        || (currentUser.TenantId is { } tenantId
            && services.GetService(typeof(ITenantDirectory)) is ITenantDirectory tenants
            && await tenants.IsSystemTenantAsync(tenantId, cancellationToken));

    private bool TryScopeKey(SettingScope scope, out Guid? key, out Error? error)
    {
        key = scope switch
        {
            SettingScope.Tenant => currentUser.TenantId,
            SettingScope.User => currentUser.UserId,
            _ => null,
        };
        error = null;
        if (scope != SettingScope.Global && key is null)
        {
            error = Error.Validation(nameof(scope), $"No {scope.ToString().ToLowerInvariant()} in the current context.");
            return false;
        }

        return true;
    }
}

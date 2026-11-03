using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Settings;

internal sealed class SettingValueHandlers(
    CoworkeeDbContext db, ICurrentUser currentUser, ISettingDefinitionManager definitions, SettingWriter writer, ISettingProvider provider, IPermissionChecker permissions,
    IServiceProvider services)
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

        // a setting with its own permission (the head script) changes only with that permission on top
        foreach (var name in values.Keys)
        {
            if (definitions.Find(name)?.Permission is { } permission && !await permissions.IsGrantedAsync(permission, cancellationToken))
            {
                return Error.Forbidden("settings.permission", $"Changing '{name}' needs the permission {permission}.");
            }
        }

        return await writer.WriteAsync(scope, key, values, cancellationToken) is { } invalid ? invalid : Result.Success();
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

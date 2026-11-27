using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Coworkee.Settings;

internal sealed class ClientValueHandlers(CoworkeeDbContext db, ICurrentUser currentUser, IPermissionChecker permissions, IOptions<ClientValueOptions> options)
    : IHandler<GetClientValues, Result<IReadOnlyList<ClientValueDto>>>,
      IHandler<GetClientValueKeys, Result<IReadOnlyList<string>>>,
      IHandler<GetClientValue, Result<ClientValueDto>>,
      IHandler<SetClientValue, Result>,
      IHandler<DeleteClientValue, Result>
{
    private static readonly Error SignInRequired = Error.Unauthorized("client_values.sign_in", "Client values belong to a signed-in user of a tenant.");

    public async Task<Result<IReadOnlyList<ClientValueDto>>> HandleAsync(GetClientValues query, CancellationToken cancellationToken) =>
        SignedIn ? await MergedAsync(null, cancellationToken) : SignInRequired;

    public async Task<Result<IReadOnlyList<string>>> HandleAsync(GetClientValueKeys query, CancellationToken cancellationToken) =>
        SignedIn ? (await MergedAsync(null, cancellationToken)).Select(v => v.Key).ToList() : SignInRequired;

    public async Task<Result<ClientValueDto>> HandleAsync(GetClientValue query, CancellationToken cancellationToken) =>
        SignedIn ? (await MergedAsync(query.Key, cancellationToken)).SingleOrDefault() ?? new ClientValueDto(query.Key, null, false) : SignInRequired;

    public async Task<Result> HandleAsync(SetClientValue command, CancellationToken cancellationToken)
    {
        var owner = await OwnerAsync(command.Scope, cancellationToken);
        if (!owner.IsSuccess)
        {
            return owner.Error!;
        }

        var userId = owner.Value;
        var row = await db.Set<ClientValue>().FirstOrDefaultAsync(v => v.UserId == userId && v.Key == command.Key, cancellationToken);
        if (row is not null)
        {
            row.Value = command.Value;
            return Result.Success();
        }

        var max = options.Value.MaxKeysPerUser;
        if (userId is { } user && await db.Set<ClientValue>().CountAsync(v => v.UserId == user, cancellationToken) >= max)
        {
            return Error.Validation(nameof(command.Key), $"A user keeps at most {max} client values; delete one first.");
        }

        db.Add(new ClientValue { UserId = userId, Key = command.Key, Value = command.Value });
        return Result.Success();
    }

    public async Task<Result> HandleAsync(DeleteClientValue command, CancellationToken cancellationToken)
    {
        var owner = await OwnerAsync(command.Scope, cancellationToken);
        if (!owner.IsSuccess)
        {
            return owner.Error!;
        }

        var userId = owner.Value;
        if (await db.Set<ClientValue>().FirstOrDefaultAsync(v => v.UserId == userId && v.Key == command.Key, cancellationToken) is { } row)
        {
            db.Remove(row);
        }

        return Result.Success();
    }

    // the tenant comes from the query filter; without one there would be nothing to read and no tenant to write to
    private bool SignedIn => currentUser is { UserId: not null, TenantId: not null };

    // the caller's id for his own value; null (the global value) with ManageGlobalClientValues
    private async Task<Result<Guid?>> OwnerAsync(ClientValueScope scope, CancellationToken cancellationToken)
    {
        if (!SignedIn)
        {
            return SignInRequired;
        }

        if (scope == ClientValueScope.User)
        {
            return Result<Guid?>.Success(currentUser.UserId);
        }

        return await permissions.IsGrantedAsync(SettingsPermissions.ManageGlobalClientValues, cancellationToken)
            ? Result<Guid?>.Success(null)
            : Error.Forbidden("client_values.global_forbidden", $"Changing a client value of every user needs the permission {SettingsPermissions.ManageGlobalClientValues}.");
    }

    // the organisation's global values and the caller's own; per key the own value wins
    private async Task<List<ClientValueDto>> MergedAsync(string? key, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var rows = await db.Set<ClientValue>().AsNoTracking()
            .Where(v => (v.UserId == null || v.UserId == userId) && (key == null || v.Key == key))
            .Select(v => new { v.Key, v.Value, Own = v.UserId != null })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.Key, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(r => r.Own).First())
            .OrderBy(r => r.Key, StringComparer.Ordinal)
            .Select(r => new ClientValueDto(r.Key, r.Value, r.Own))
            .ToList();
    }
}

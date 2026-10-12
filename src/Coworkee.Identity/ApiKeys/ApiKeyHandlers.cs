using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Coworkee.Identity.ApiKeys;

internal sealed class ApiKeyHandlers(
    CoworkeeDbContext db, ICurrentUser currentUser, IPermissionChecker permissions, IPermissionDefinitionManager definitions, TimeProvider clock,
    IOptions<ApiKeyOptions> options)
    : IHandler<GetMyApiKeys, Result<IReadOnlyList<ApiKeyDto>>>, IHandler<GetApiKeyScopes, Result<IReadOnlyList<PermissionGroupDto>>>,
      IHandler<CreateApiKey, Result<CreatedApiKeyDto>>, IHandler<RevokeApiKey, Result>
{
    private static readonly Error FromKey = Error.Forbidden("api_keys.from_key", "API keys cannot manage API keys; sign in to the app.");

    private static readonly Error NotFound = Error.NotFound("api_keys.not_found", "The API key does not exist.");

    private Guid Me => currentUser.UserId ?? throw new InvalidOperationException("No signed-in user.");

    public async Task<Result<IReadOnlyList<ApiKeyDto>>> HandleAsync(GetMyApiKeys query, CancellationToken cancellationToken)
    {
        var keys = await db.Set<ApiKey>().AsNoTracking().Where(k => k.UserId == Me).OrderByDescending(k => k.CreatedAt).ToListAsync(cancellationToken);
        return keys.Select(k => k.ToDto()).ToList();
    }

    /// <summary>
    /// The permissions the user may give a key: what the session has right now, within the chosen active role. Grouped like the
    /// definitions; the others stay unknown to the user.
    /// </summary>
    public async Task<Result<IReadOnlyList<PermissionGroupDto>>> HandleAsync(GetApiKeyScopes query, CancellationToken cancellationToken)
    {
        var delegable = await permissions.GetGrantedAsync(cancellationToken);
        return definitions.Groups
            .Select(g => new PermissionGroupDto(g.Name, g.DisplayName, [.. definitions.All
                .Where(d => d.Group == g.Name && delegable.Contains(d.Name))
                .Select(d => new PermissionDto(d.Name, d.DisplayName, d.Implies))]))
            .Where(g => g.Permissions.Count > 0)
            .ToList();
    }

    public async Task<Result<CreatedApiKeyDto>> HandleAsync(CreateApiKey command, CancellationToken cancellationToken)
    {
        if (currentUser.ApiKeyId is not null)
        {
            return FromKey;
        }

        var now = clock.GetUtcNow();
        var request = command.Key;
        if (request.ExpiresAt <= now || request.ExpiresAt > now.AddDays(ApiKeyDefaults.MaxLifetimeDays))
        {
            return Error.Validation(nameof(request.ExpiresAt), $"Pick an expiry within the next {ApiKeyDefaults.MaxLifetimeDays} days.");
        }

        if (await db.Set<ApiKey>().CountAsync(k => k.UserId == Me && k.RevokedAt == null && k.ExpiresAt > now, cancellationToken) >= options.Value.MaxActivePerUser)
        {
            return Error.Conflict("api_keys.limit", $"You have {options.Value.MaxActivePerUser} active keys; revoke one first.");
        }

        var delegable = await permissions.GetGrantedAsync(cancellationToken);
        var scopes = request.Scopes.Distinct(StringComparer.Ordinal).ToList();
        if (scopes.Where(s => !delegable.Contains(s)).ToList() is { Count: > 0 } missing)
        {
            return Error.Validation(nameof(request.Scopes), $"You cannot grant permissions you do not have: {string.Join(", ", missing)}.");
        }

        var (secret, prefix, hash) = ApiKeySecret.Create();
        var key = new ApiKey
        {
            TenantId = currentUser.TenantId ?? Guid.Empty,
            UserId = Me,
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Prefix = prefix,
            SecretHash = hash,
            Scopes = scopes,
            ExpiresAt = request.ExpiresAt,
            CreatedAt = now,
            CreatedBy = Me,
        };
        db.Add(key);
        return new CreatedApiKeyDto(key.ToDto(), secret);
    }

    public async Task<Result> HandleAsync(RevokeApiKey command, CancellationToken cancellationToken)
    {
        if (currentUser.ApiKeyId is not null)
        {
            return FromKey;
        }

        var key = await db.Set<ApiKey>().SingleOrDefaultAsync(k => k.Id == command.Id, cancellationToken);
        if (key is null || (key.UserId != Me && !await permissions.IsGrantedAsync(ApiKeyPermissions.Manage, cancellationToken)))
        {
            return NotFound;
        }

        if (key.RevokedAt is null)
        {
            key.RevokedAt = clock.GetUtcNow();
            key.RevokedBy = Me;
        }

        return Result.Success();
    }
}

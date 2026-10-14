using Coworkee.Contracts.Mailing;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Mailing;
using Coworkee.OData;
using Coworkee.Settings;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.OData;

namespace Coworkee.Account;

internal sealed class MailRecipientDirectory(IServiceProvider services, CoworkeeDbContext db, TimeProvider clock) : IMailRecipientDirectory
{
    public async Task<Result<RecipientPage>> PageAsync(RecipientSourceDto source, Guid? after, int size, CancellationToken cancellationToken)
    {
        var resolved = await EligibleAsync(source, cancellationToken);
        if (!resolved.IsSuccess)
        {
            return resolved.Error;
        }

        var (_, eligible) = resolved.Value;
        var rows = await eligible.Where(u => after == null || u.Id.CompareTo(after.Value) > 0).OrderBy(u => u.Id).Take(size + 1)
            .Select(u => new { u.Id, u.Email, u.FirstName, u.LastName, u.TenantId }).ToListAsync(cancellationToken);
        var page = rows.Take(size).ToList();
        var cultures = await RecipientCultures.ForAsync(db, page.ToDictionary(u => u.Id, u => (Guid?)u.TenantId), cancellationToken);
        return new RecipientPage(
            [.. page.Select(u => new MailRecipient(u.Id, u.Email!, u.FirstName, u.LastName, cultures.GetValueOrDefault(u.Id)?.Name))],
            rows.Count > size ? page[^1].Id : null);
    }

    public async Task<Result<RecipientPreviewDto>> PreviewAsync(RecipientSourceDto source, int first, CancellationToken cancellationToken)
    {
        var resolved = await EligibleAsync(source, cancellationToken);
        if (!resolved.IsSuccess)
        {
            return resolved.Error;
        }

        var (selected, eligible) = resolved.Value;
        var count = await eligible.CountAsync(cancellationToken);
        var excluded = await selected.CountAsync(cancellationToken) - count;
        // without the right to read users the counts stay, the names do not
        var visibleRows = await ODataEntityAccess.IsGrantedAsync(services, typeof(User), cancellationToken) ? first : 0;
        var rows = await eligible.OrderBy(u => u.Email).ThenBy(u => u.Id).Take(visibleRows)
            .Select(u => new { u.Id, u.Email, u.FirstName, u.LastName, u.LastLoginAt }).ToListAsync(cancellationToken);
        return new RecipientPreviewDto(count, excluded,
            [.. rows.Select(u => new RecipientRowDto(u.Id, u.Email!, $"{u.FirstName} {u.LastName}".Trim() is { Length: > 0 } name ? name : null, u.LastLoginAt))]);
    }

    private async Task<Result<(IQueryable<User> Selected, IQueryable<User> Eligible)>> EligibleAsync(RecipientSourceDto source, CancellationToken cancellationToken)
    {
        var selected = await SelectAsync(source, cancellationToken);
        if (!selected.IsSuccess)
        {
            return selected.Error;
        }

        var now = clock.GetUtcNow();
        var eligible = selected.Value.Where(u => u.Email != null && u.Email != string.Empty && (u.LockoutEnd == null || u.LockoutEnd <= now));
        if (source is not { Kind: RecipientSourceKind.Preset, Preset: RecipientPreset.Inactive })
        {
            eligible = eligible.Where(u => u.IsActive);
        }

        return (selected.Value, await WithoutOptedOutAsync(eligible, cancellationToken));
    }

    private async Task<Result<IQueryable<User>>> SelectAsync(RecipientSourceDto source, CancellationToken cancellationToken)
    {
        if (source.Kind is RecipientSourceKind.Query or RecipientSourceKind.Manual && !await ODataEntityAccess.IsGrantedAsync(services, typeof(User), cancellationToken))
        {
            return Error.Forbidden("mail.recipients_forbidden", "Choosing recipients by filter or by hand requires the right to view users.");
        }

        switch (source.Kind)
        {
            case RecipientSourceKind.Preset:
                return await PresetAsync(source, cancellationToken);
            case RecipientSourceKind.Query:
                try
                {
                    return Result<IQueryable<User>>.Success(await ODataEntityAccess.FilteredAsync<User>(services, source.Filter, cancellationToken));
                }
                catch (ODataException exception)
                {
                    return Error.Validation(nameof(RecipientSourceDto.Filter), exception.Message);
                }

            case RecipientSourceKind.Manual:
                var ids = source.UserIds?.ToList() ?? [];
                return Result<IQueryable<User>>.Success((await ODataEntityAccess.VisibleAsync<User>(services, cancellationToken)).Where(u => ids.Contains(u.Id)));
            default:
                return Error.Validation(nameof(RecipientSourceDto.Kind), "Choose a recipient source.");
        }
    }

    private async Task<Result<IQueryable<User>>> PresetAsync(RecipientSourceDto source, CancellationToken cancellationToken)
    {
        var users = await ODataEntityAccess.VisibleAsync<User>(services, cancellationToken);
        var days = source.Days is > 0 ? source.Days.Value : 0;
        var cutoff = clock.GetUtcNow().AddDays(-days);
        if (source.Preset is RecipientPreset.NotSignedInSince or RecipientPreset.SignedUpWithin && days == 0)
        {
            return Error.Validation(nameof(RecipientSourceDto.Days), "Days must be greater than 0.");
        }

        switch (source.Preset)
        {
            case RecipientPreset.AllActive:
                return Result<IQueryable<User>>.Success(users);
            case RecipientPreset.NotSignedInSince:
                return Result<IQueryable<User>>.Success(users.Where(u => u.LastLoginAt < cutoff || (u.LastLoginAt == null && u.CreatedAt < cutoff)));
            case RecipientPreset.NeverSignedIn:
                return Result<IQueryable<User>>.Success(users.Where(u => u.LastLoginAt == null));
            case RecipientPreset.SignedUpWithin:
                return Result<IQueryable<User>>.Success(users.Where(u => u.CreatedAt >= cutoff));
            case RecipientPreset.EmailNotConfirmed:
                return Result<IQueryable<User>>.Success(users.Where(u => !u.EmailConfirmed));
            case RecipientPreset.Inactive:
                return Result<IQueryable<User>>.Success(users.Where(u => !u.IsActive));
            case RecipientPreset.InRoles:
                var roleIds = source.RoleIds ?? [];
                if (roleIds.Count == 0)
                {
                    return Error.Validation(nameof(RecipientSourceDto.RoleIds), "Choose at least one role.");
                }

                var members = db.Set<IdentityUserRole<Guid>>().Where(r => roleIds.Contains(r.RoleId)).Select(r => r.UserId)
                    .Union(from member in db.Set<UserGroupMember>()
                           join role in db.Set<UserGroupRole>() on member.GroupId equals role.GroupId
                           where roleIds.Contains(role.RoleId)
                           select member.UserId);
                return Result<IQueryable<User>>.Success(users.Where(u => members.Contains(u.Id)));
            default:
                return Error.Validation(nameof(RecipientSourceDto.Preset), "Choose a preset.");
        }
    }

    private async Task<IQueryable<User>> WithoutOptedOutAsync(IQueryable<User> users, CancellationToken cancellationToken)
    {
        // bool settings may be stored in any casing
        var name = MailSettings.Newsletters;
        var globallyOff = await db.Set<SettingValue>().AnyAsync(v => v.Name == name && v.Scope == SettingScope.Global && v.Value!.ToLower() == "false", cancellationToken);
        var optedIn = db.Set<SettingValue>().Where(v => v.Name == name && v.Scope == SettingScope.User && v.Value!.ToLower() == "true").Select(v => v.ScopeKey!.Value);
        var optedOut = db.Set<SettingValue>().Where(v => v.Name == name && v.Scope == SettingScope.User && v.Value!.ToLower() == "false").Select(v => v.ScopeKey!.Value);
        return globallyOff ? users.Where(u => optedIn.Contains(u.Id)) : users.Where(u => !optedOut.Contains(u.Id));
    }
}

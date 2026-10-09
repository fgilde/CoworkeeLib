using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Localization;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Settings;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Account.Users;

// the language is the user's own setting; administrators read and write it for users of their organisation
internal sealed class UserLanguageHandlers(CoworkeeDbContext db, ICurrentUser currentUser)
    : IHandler<GetUserLanguage, Result<UserLanguageDto>>, IHandler<SetUserLanguage, Result>
{
    public async Task<Result<UserLanguageDto>> HandleAsync(GetUserLanguage query, CancellationToken cancellationToken) =>
        await InTenantAsync(query.Id, cancellationToken)
            ? new UserLanguageDto(await Stored(query.Id).Select(s => s.Value).SingleOrDefaultAsync(cancellationToken))
            : AccountErrors.UserNotFound;

    public async Task<Result> HandleAsync(SetUserLanguage command, CancellationToken cancellationToken)
    {
        if (!await InTenantAsync(command.Id, cancellationToken))
        {
            return AccountErrors.UserNotFound;
        }

        var stored = await Stored(command.Id).SingleOrDefaultAsync(cancellationToken);
        if (stored is not null && command.Culture is null)
        {
            db.Remove(stored);
        }
        else if (stored is not null)
        {
            stored.Value = command.Culture;
        }
        else if (command.Culture is not null)
        {
            db.Add(new SettingValue { Name = LocalizationSettings.Culture, Scope = SettingScope.User, ScopeKey = command.Id, Value = command.Culture });
        }

        return Result.Success();
    }

    private Task<bool> InTenantAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<User>().AnyAsync(u => u.Id == id && u.TenantId == currentUser.TenantId, cancellationToken);

    private IQueryable<SettingValue> Stored(Guid userId) =>
        db.Set<SettingValue>().Where(s => s.Name == LocalizationSettings.Culture && s.Scope == SettingScope.User && s.ScopeKey == userId);
}

using Coworkee.Application.Messaging;
using Coworkee.Application.Privacy;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Users.Privacy;

// ponytail: the export is built synchronously in memory; a background job with a download link pays off once a user's data reaches many megabytes
internal sealed class PersonalDataHandlers(CoworkeeDbContext db, ICurrentUser currentUser, IEnumerable<IPersonalDataContributor> contributors)
    : IHandler<ExportMyPersonalData, Result<IReadOnlyDictionary<string, object>>>, IHandler<DeleteMyAccount, Result>, IHandler<DeleteUser, Result>
{
    public async Task<Result<IReadOnlyDictionary<string, object>>> HandleAsync(ExportMyPersonalData query, CancellationToken cancellationToken)
    {
        if (await SubjectAsync(currentUser.UserId, cancellationToken) is not { } subject)
        {
            return UserErrors.NotFound;
        }

        var sections = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var contributor in contributors)
        {
            if (await contributor.ExportAsync(subject, cancellationToken) is { } section)
            {
                sections[contributor.Section] = section;
            }
        }

        return Result<IReadOnlyDictionary<string, object>>.Success(sections);
    }

    public async Task<Result> HandleAsync(DeleteMyAccount command, CancellationToken cancellationToken)
    {
        if (await SubjectAsync(currentUser.UserId, cancellationToken) is not { } subject)
        {
            return UserErrors.NotFound;
        }

        return string.Equals(subject.Email, command.Email.Trim(), StringComparison.OrdinalIgnoreCase)
            ? await EraseAsync(subject, cancellationToken)
            : Error.Validation(nameof(command.Email), "The e-mail address does not match your account.");
    }

    public async Task<Result> HandleAsync(DeleteUser command, CancellationToken cancellationToken)
    {
        if (await SubjectAsync(command.Id, cancellationToken) is not { } subject)
        {
            return UserErrors.NotFound;
        }

        if (await AdminGuard.IsAdminAsync(db, subject.UserId, cancellationToken) && !await AdminGuard.IsAdminAsync(db, currentUser.UserId, cancellationToken))
        {
            return Error.Forbidden("identity.admin_role_restricted", "Only administrators can delete an administrator.");
        }

        return await EraseAsync(subject, cancellationToken);
    }

    private async Task<Result> EraseAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        if (await AdminGuard.IsAdminAsync(db, subject.UserId, cancellationToken)
            && !await AdminGuard.OtherActiveAdminExistsAsync(db, subject.TenantId, subject.UserId, cancellationToken))
        {
            return AdminGuard.LastAdmin;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var contributor in contributors)
        {
            await contributor.EraseAsync(subject, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }

    private Task<PersonalDataSubject?> SubjectAsync(Guid? userId, CancellationToken cancellationToken) =>
        db.Set<User>().AsNoTracking()
            .Where(u => u.Id == userId && u.TenantId == currentUser.TenantId)
            .Select(u => new PersonalDataSubject(u.Id, u.TenantId, u.Email))
            .SingleOrDefaultAsync(cancellationToken);
}

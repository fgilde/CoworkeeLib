using System.Text.Json;
using Coworkee.Contracts.Data;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.OData.Transfer.Import;

/// <summary>Saved column mappings per set, shared in the organisation; saving a name again replaces the profile.</summary>
public sealed class ImportProfiles(CoworkeeDbContext db, ImportService imports, ICurrentUser user)
{
    public async Task<Result<IReadOnlyList<ImportProfileDto>>> ListAsync(string entitySet, CancellationToken cancellationToken)
    {
        var import = await imports.FindAsync(entitySet, cancellationToken);
        if (!import.IsSuccess)
        {
            return import.Error!;
        }

        var profiles = await db.Set<ImportProfile>().AsNoTracking().Where(p => p.EntitySet == import.Value.EntitySet).OrderBy(p => p.Name).ToListAsync(cancellationToken);
        return profiles.Select(Dto).ToList();
    }

    public async Task<Result<ImportProfileDto>> SaveAsync(string entitySet, SaveImportProfileRequest request, CancellationToken cancellationToken)
    {
        var import = await imports.FindAsync(entitySet, cancellationToken);
        if (!import.IsSuccess)
        {
            return import.Error!;
        }

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200)
        {
            return Error.Validation(nameof(request.Name), "Give the profile a name of at most 200 characters.");
        }

        var name = request.Name.Trim();
        var profile = await db.Set<ImportProfile>().SingleOrDefaultAsync(p => p.EntitySet == import.Value.EntitySet && p.Name == name, cancellationToken);
        if (profile is null)
        {
            profile = new ImportProfile { TenantId = user.TenantId ?? Guid.Empty, EntitySet = import.Value.EntitySet, Name = name, Columns = "{}" };
            db.Add(profile);
        }

        profile.Columns = JsonSerializer.Serialize(request.Columns);
        (profile.Mode, profile.KeyField, profile.Culture) = (request.Mode, request.KeyField, request.Culture);
        await db.SaveChangesAsync(cancellationToken);
        return Dto(profile);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await db.Set<ImportProfile>().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) is not { } profile)
        {
            return Error.NotFound("import.profile_not_found", "There is no such profile.");
        }

        if (await imports.FindAsync(profile.EntitySet, cancellationToken) is { IsSuccess: false } denied)
        {
            return denied.Error!;
        }

        db.Remove(profile);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static ImportProfileDto Dto(ImportProfile profile) =>
        new(profile.Id, profile.EntitySet, profile.Name, JsonSerializer.Deserialize<Dictionary<string, string>>(profile.Columns) ?? [], profile.Mode, profile.KeyField, profile.Culture);
}

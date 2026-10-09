using Coworkee.Application.Privacy;
using Coworkee.Contracts.Settings;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Settings;

internal sealed class SettingPersonalData(CoworkeeDbContext db, ISettingDefinitionManager definitions) : IPersonalDataContributor
{
    public string Section => "settings";

    public async Task<object?> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken) =>
        (await Mine(subject).AsNoTracking().OrderBy(v => v.Name).ToListAsync(cancellationToken))
            .Select(v => new { v.Name, Value = definitions.Find(v.Name)?.IsEncrypted == true ? "***" : v.Value, v.ModifiedAt })
            .ToList();

    public Task EraseAsync(PersonalDataSubject subject, CancellationToken cancellationToken) => Mine(subject).ExecuteDeleteAsync(cancellationToken);

    private IQueryable<SettingValue> Mine(PersonalDataSubject subject) =>
        db.Set<SettingValue>().Where(v => v.Scope == SettingScope.User && v.ScopeKey == subject.UserId);
}

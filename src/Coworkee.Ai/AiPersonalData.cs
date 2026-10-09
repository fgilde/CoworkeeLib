using Coworkee.Application.Privacy;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Ai;

/// <summary>The assistant keeps no chat history; the tool call log holds what the user had it do.</summary>
internal sealed class AiPersonalData(CoworkeeDbContext db) : IPersonalDataContributor
{
    public string Section => "aiToolCalls";

    public async Task<object?> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken) =>
        await Mine(subject).AsNoTracking().OrderBy(c => c.At)
            .Select(c => new { c.Tool, c.Channel, c.Input, c.Succeeded, c.Error, c.At })
            .ToListAsync(cancellationToken);

    public Task EraseAsync(PersonalDataSubject subject, CancellationToken cancellationToken) => Mine(subject).ExecuteDeleteAsync(cancellationToken);

    private IQueryable<AiToolCall> Mine(PersonalDataSubject subject) => db.Set<AiToolCall>().Where(c => c.UserId == subject.UserId);
}

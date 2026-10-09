using Coworkee.Application.Privacy;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Social;

/// <summary>Own comments and ratings; erasing removes them like deleting a comment does, replies included.</summary>
internal sealed class SocialPersonalData(CoworkeeDbContext db) : IPersonalDataContributor
{
    public string Section => "social";

    public async Task<object?> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken) => new
    {
        Comments = await Comments(subject).AsNoTracking().OrderBy(c => c.CreatedAt)
            .Select(c => new { c.EntityType, c.EntityId, c.Text, c.CreatedAt, c.EditedAt }).ToListAsync(cancellationToken),
        Ratings = await Ratings(subject).AsNoTracking().Select(r => new { r.EntityType, r.EntityId, r.Stars }).ToListAsync(cancellationToken),
    };

    public async Task EraseAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        await Comments(subject).ExecuteDeleteAsync(cancellationToken);
        await Ratings(subject).ExecuteDeleteAsync(cancellationToken);
    }

    private IQueryable<Comment> Comments(PersonalDataSubject subject) => db.Set<Comment>().IgnoreQueryFilters().Where(c => c.AuthorId == subject.UserId);

    private IQueryable<Rating> Ratings(PersonalDataSubject subject) => db.Set<Rating>().IgnoreQueryFilters().Where(r => r.UserId == subject.UserId);
}

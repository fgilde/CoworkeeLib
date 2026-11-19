using Coworkee.Application.Privacy;
using Coworkee.Contracts.Realtime;
using Coworkee.Contracts.Settings;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nextended.Core.Attributes;

namespace Coworkee.Settings;

/// <summary>
/// A free value a client stores under a key (a list layout, a template, a preset): the user's own (<see cref="UserId"/>) or the organisation's global value
/// for every user (no user). Unlike a setting it needs no definition; the key is the client's business.
/// </summary>
public sealed class ClientValue : AuditedEntity, IMultiTenant
{
    public const int KeyLength = 256;

    public Guid TenantId { get; set; }

    public Guid? UserId { get; set; }

    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}

/// <summary>Limits of the client values (<c>Coworkee:ClientValues</c>).</summary>
public sealed class ClientValueOptions
{
    public const string Section = "Coworkee:ClientValues";

    /// <summary>The largest value in bytes (UTF-8); a larger one is refused with 400.</summary>
    public int MaxValueBytes { get; set; } = 64 * 1024;

    /// <summary>The most keys of one user's own values in an organisation; a new key beyond is refused with 400, existing keys still change.</summary>
    public int MaxKeysPerUser { get; set; } = 1000;
}

/// <summary>A user's own value goes to that user's topic only, a global value to the organisation (<see cref="SettingTopics.ClientValues"/>).</summary>
[RegisterAs(typeof(IRealtimeTopicMapper), ServiceLifetime = ServiceLifetime.Singleton)]
internal sealed class ClientValueTopics : IRealtimeTopicMapper
{
    public IEnumerable<string> TopicsFor(object entity) => entity switch
    {
        ClientValue { UserId: { } user } => [RealtimeTopics.User(user)],
        ClientValue => [SettingTopics.ClientValues],
        _ => [],
    };
}

/// <summary>The user's own client values: exported, and deleted with him; the organisation's global values stay.</summary>
internal sealed class ClientValuePersonalData(CoworkeeDbContext db) : IPersonalDataContributor
{
    public string Section => "clientValues";

    public async Task<object?> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken) =>
        await Mine(subject).AsNoTracking().OrderBy(v => v.Key).Select(v => new { v.Key, v.Value, v.ModifiedAt }).ToListAsync(cancellationToken);

    public Task EraseAsync(PersonalDataSubject subject, CancellationToken cancellationToken) => Mine(subject).ExecuteDeleteAsync(cancellationToken);

    // a user id is one user of one organisation: his rows wherever they are
    private IQueryable<ClientValue> Mine(PersonalDataSubject subject) => db.Set<ClientValue>().IgnoreQueryFilters().Where(v => v.UserId == subject.UserId);
}

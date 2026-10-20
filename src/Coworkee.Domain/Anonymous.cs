namespace Coworkee.Domain;

/// <summary>
/// Rows that must not be tied to their author, e.g. answers of an anonymous survey: no CreatedBy/ModifiedBy/DeletedBy, no audit trail,
/// no user on outbox, webhook and workflow trigger messages, and creation, change and deletion times cut to <see cref="TimestampPrecision"/>.
/// Ids must not be time ordered either: derive from <see cref="AnonymousEntity"/> or set <c>Id = Guid.NewGuid()</c> in the constructor.
/// </summary>
public interface IAnonymous
{
    /// <summary>Times the library writes are cut to this (UTC); <see cref="TimeSpan.Zero"/> keeps them exact. Change it with an explicit implementation, e.g. <c>TimeSpan IAnonymous.TimestampPrecision =&gt; TimeSpan.FromHours(1);</c> (list <see cref="IAnonymous"/> on the class again when it derives from <see cref="AnonymousEntity"/>).</summary>
    TimeSpan TimestampPrecision => TimeSpan.FromDays(1);
}

/// <summary>An <see cref="IAnonymous"/> entity with a random id (<see cref="Guid.NewGuid"/>) instead of a time ordered one.</summary>
public abstract class AnonymousEntity : Entity, IAnonymous
{
    protected AnonymousEntity() => Id = Guid.NewGuid();
}

public static class AnonymousExtensions
{
    /// <summary>The time cut to the record's precision, for own timestamps too: <c>response.SubmittedAt = response.Truncate(clock.GetUtcNow())</c>.</summary>
    public static DateTimeOffset Truncate(this IAnonymous record, DateTimeOffset time)
    {
        var precision = record.TimestampPrecision.Ticks;
        return precision <= 0 ? time : new DateTimeOffset(time.UtcTicks - (time.UtcTicks % precision), TimeSpan.Zero);
    }
}

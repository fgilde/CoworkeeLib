namespace Coworkee.Domain;

public static class AnonymousExtensions
{
    /// <summary>The time cut to the record's precision, for own timestamps too: <c>response.SubmittedAt = response.Truncate(clock.GetUtcNow())</c>.</summary>
    public static DateTimeOffset Truncate(this IAnonymous record, DateTimeOffset time)
    {
        var precision = record.TimestampPrecision.Ticks;
        return precision <= 0 ? time : new DateTimeOffset(time.UtcTicks - (time.UtcTicks % precision), TimeSpan.Zero);
    }
}

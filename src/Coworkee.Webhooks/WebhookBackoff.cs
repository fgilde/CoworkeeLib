namespace Coworkee.Webhooks;

public static class WebhookBackoff
{
    /// <summary>The wait after failed attempt <paramref name="attempt"/> (1 based): the first delay, doubled per attempt, capped.</summary>
    public static TimeSpan Delay(int attempt, WebhookOptions options) =>
        TimeSpan.FromTicks((long)Math.Min(options.MaxRetryDelay.Ticks, options.FirstRetryDelay.Ticks * Math.Pow(2, attempt - 1)));
}

using Microsoft.AspNetCore.SignalR.Client;

namespace Coworkee.Client.Blazor.Realtime;

public sealed class ForeverRetryPolicy : IRetryPolicy
{
    private static readonly TimeSpan[] Delays = [TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    public TimeSpan? NextRetryDelay(RetryContext retryContext) => Delays[Math.Min(retryContext.PreviousRetryCount, Delays.Length - 1)];
}

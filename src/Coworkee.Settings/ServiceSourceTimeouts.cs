namespace Coworkee.Settings;

internal static class ServiceSourceTimeouts
{
    /// <summary>What a built-in source may take for one request or probe.</summary>
    public static readonly TimeSpan Request = TimeSpan.FromSeconds(3);

    /// <summary>What the directory waits for any source: only a guard for hanging custom sources, so it must stay well above <see cref="Request"/>.</summary>
    public static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    /// <summary>A token that is cancelled with <paramref name="cancellationToken"/> or after <paramref name="limit"/>.</summary>
    public static Limited Limit(TimeSpan limit, TimeProvider time, CancellationToken cancellationToken) => new(limit, time, cancellationToken);

    internal sealed class Limited : IDisposable
    {
        private readonly CancellationTokenSource _limit;
        private readonly CancellationTokenSource _linked;

        public Limited(TimeSpan limit, TimeProvider time, CancellationToken cancellationToken)
        {
            _limit = new CancellationTokenSource(limit, time);
            _linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _limit.Token);
        }

        public CancellationToken Token => _linked.Token;

        public void Dispose()
        {
            _linked.Dispose();
            _limit.Dispose();
        }
    }
}

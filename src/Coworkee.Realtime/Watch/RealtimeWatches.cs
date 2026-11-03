using System.Collections.Concurrent;

namespace Coworkee.Realtime.Watch;

/// <summary>The watched sets of the app with where each look starts and when it is due; the watcher reads them, tests count the looks.</summary>
internal sealed class RealtimeWatches(IEnumerable<IRealtimeWatch> watches)
{
    private readonly ConcurrentDictionary<Type, State> _states = new(watches.ToDictionary(w => w.EntityType, w => new State(w)));

    public IEnumerable<State> All => _states.Values;

    /// <summary>The looks of the watch of <paramref name="entityType"/> so far.</summary>
    public int Polls(Type entityType) => _states.TryGetValue(entityType, out var state) ? state.Polls : 0;

    internal sealed class State(IRealtimeWatch watch)
    {
        private int _polls;

        public IRealtimeWatch Watch { get; } = watch;

        /// <summary>The newest date seen; null while nobody follows the set (the next look starts at the newest date then).</summary>
        public DateTimeOffset? Watermark { get; set; }

        public DateTimeOffset Due { get; set; }

        public int Polls => _polls;

        public void Polled() => Interlocked.Increment(ref _polls);
    }
}

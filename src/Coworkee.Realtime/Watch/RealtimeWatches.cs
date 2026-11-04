using System.Collections.Concurrent;

namespace Coworkee.Realtime.Watch;

/// <summary>The watched sets of the app with where each look starts and when it is due; the watcher reads them, tests count the looks.</summary>
internal sealed class RealtimeWatches(IEnumerable<IRealtimeWatch> watches)
{
    private readonly ConcurrentDictionary<Type, State> _states = new(watches.ToDictionary(w => w.EntityType, w => new State(w)));

    public IEnumerable<State> All => _states.Values;

    /// <summary>The watch of the entity type named so (the topics' name of the set); null when the type is not watched.</summary>
    public State? Find(string entityType) => _states.Values.FirstOrDefault(s => string.Equals(s.Watch.EntityType.Name, entityType, StringComparison.OrdinalIgnoreCase));

    /// <summary>The looks of the watch of <paramref name="entityType"/> so far.</summary>
    public int Polls(Type entityType) => _states.TryGetValue(entityType, out var state) ? state.Polls : 0;

    internal sealed class State(IRealtimeWatch watch)
    {
        private int _polls;

        public IRealtimeWatch Watch { get; } = watch;

        /// <summary>Switched off (RealtimePermissions.ManageWatches), the watch reads nothing, as if nobody followed the set.</summary>
        // shortcut: kept per instance (an instance restarted watches again); a setting shared by the instances when they run several
        public bool Enabled { get; set; } = true;

        /// <summary>The newest date seen; null while nobody follows the set (the next look starts at the newest date then).</summary>
        public DateTimeOffset? Watermark { get; set; }

        public DateTimeOffset Due { get; set; }

        public int Polls => _polls;

        public void Polled() => Interlocked.Increment(ref _polls);
    }
}

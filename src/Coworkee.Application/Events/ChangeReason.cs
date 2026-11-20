namespace Coworkee.Application.Events;

/// <summary>
/// Why things are saved: <c>using (ChangeReason.Begin("Import from CW1")) { ... }</c> puts the reason on every <see cref="EntityChange"/> and audit entry saved
/// inside, also in nested calls. A request with <see cref="IHasChangeReason"/> opens the scope for its handler and its save.
/// </summary>
public static class ChangeReason
{
    private static readonly AsyncLocal<string?> Ambient = new();

    public static string? Current => Ambient.Value;

    /// <summary>The reason until disposed; null or empty keeps the reason of an outer scope.</summary>
    public static IDisposable Begin(string? reason)
    {
        var previous = Ambient.Value;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            Ambient.Value = reason.Trim();
        }

        return new Restore(previous);
    }

    private sealed class Restore(string? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}

/// <summary>A request that says why it changes things (e.g. a comment field of a form): the unit of work saves with <see cref="ChangeReason.Begin"/>.</summary>
public interface IHasChangeReason
{
    string? Reason { get; }
}

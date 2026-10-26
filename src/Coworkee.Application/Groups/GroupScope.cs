namespace Coworkee.Application.Groups;

/// <summary>Ambient switches for <see cref="IGroupScope"/>; each lasts until disposed, nested scopes restore the outer value.</summary>
public static class GroupScope
{
    private static readonly AsyncLocal<bool> UnnarrowedValue = new();
    private static readonly AsyncLocal<IReadOnlyList<Guid>?> SelectionValue = new();

    public static bool IsUnnarrowed => UnnarrowedValue.Value;

    public static IReadOnlyList<Guid>? SelectionOverride => SelectionValue.Value;

    /// <summary>Access instead of listing: <see cref="IGroupScope.ActiveAsync"/> returns every accessible group.</summary>
    public static IDisposable Unnarrowed()
    {
        var previous = UnnarrowedValue.Value;
        UnnarrowedValue.Value = true;
        return new Restore(() => UnnarrowedValue.Value = previous);
    }

    /// <summary>Applies <paramref name="ids"/> as the selection, also where the stored one does not (jobs started from the UI).</summary>
    public static IDisposable Selection(IReadOnlyList<Guid> ids)
    {
        var previous = SelectionValue.Value;
        SelectionValue.Value = ids;
        return new Restore(() => SelectionValue.Value = previous);
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}

namespace Coworkee.Client.Blazor.ClientEntities;

/// <summary>Text sorts ignoring case like the server; values that differ only in case stay apart, so equality remains exact.</summary>
internal sealed class TextComparer : IComparer<string?>
{
    public static readonly TextComparer Instance = new();

    private static readonly StringComparer Culture = StringComparer.CurrentCultureIgnoreCase;

    public int Compare(string? x, string? y) => Culture.Compare(x, y) is var order and not 0 ? order : string.CompareOrdinal(x, y);
}

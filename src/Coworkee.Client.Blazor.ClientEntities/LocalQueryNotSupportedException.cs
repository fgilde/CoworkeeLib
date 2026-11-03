namespace Coworkee.Client.Blazor.ClientEntities;

/// <summary>The local copy cannot answer a query (a property the row type lacks, a function it does not know); the server can.</summary>
public sealed class LocalQueryNotSupportedException(string message, Exception inner) : NotSupportedException(message, inner)
{
    /// <summary>What a local query throws for a query it cannot answer, as opposed to a failure of the copy itself.</summary>
    internal static bool IsUnanswerable(Exception exception) =>
        exception is NotSupportedException or InvalidOperationException or ArgumentException or FormatException or InvalidCastException;
}

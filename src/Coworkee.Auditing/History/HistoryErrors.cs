using Coworkee.Core.Results;

namespace Coworkee.Auditing.History;

internal static class HistoryErrors
{
    public static readonly Error NotFound = Error.NotFound("history.not_found", "The record or the history entry does not exist.");
    public static readonly Error UnknownSet = Error.NotFound("history.unknown_set", "The entity set has no history.");
    public static readonly Error NotRestorable = Error.Validation("property", "This change cannot be undone.");
    public static readonly Error Changed = Error.Conflict("history.changed", "The record changed since the preview. Check the preview again before you restore.");
}

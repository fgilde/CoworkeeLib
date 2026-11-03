using Coworkee.Core.Results;

namespace Coworkee.Auditing;

internal static class VersionErrors
{
    public static readonly Error NotFound = Error.NotFound("versions.not_found", "The version does not exist.");
    public static readonly Error UnknownType = Error.NotFound("versions.unknown_type", "The entity type is not versioned.");
}

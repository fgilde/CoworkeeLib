using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Groups;

internal static class GroupErrors
{
    public static readonly Error NotFound = Error.NotFound("identity.group_not_found", "Group not found.");

    public static readonly Error ParentNotFound = Error.NotFound("identity.group_parent_not_found", "Parent group not found.");

    public static readonly Error TooDeep = Error.Validation(nameof(GroupRequest.ParentId), $"Groups can be nested at most {GroupCodes.MaxDepth} levels deep.");

    public static readonly Error MoveIntoOwnSubtree = new("identity.group_move_cycle", "A group cannot be moved into itself or one of its sub-groups.", ErrorKind.Validation,
        new Dictionary<string, string[]> { [nameof(GroupRequest.ParentId)] = ["A group cannot be moved into itself or one of its sub-groups."] });

    public static readonly Error ParentPathUnknown = new("identity.group_parent_path_unknown", "No group exists at this parent path.", ErrorKind.Validation,
        new Dictionary<string, string[]> { [nameof(GroupImportRow.ParentPath)] = ["No group exists at this parent path."] });

    public static readonly Error UseMoveForParent = Error.Validation(nameof(GroupRequest.ParentId),
        "An update keeps the parent; move the group with PUT /api/v1/identity/groups/{id}/parent.");

    public static readonly Error NameTaken = Error.Conflict("identity.group_name_taken", "A group with this name already exists.");
}

using Coworkee.Application.Authorization;
using Coworkee.Core.Results;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Files.Attachments;

/// <summary>Who may read (sees the record through its OData set) and write (also the write permission) the attachments of a record.</summary>
internal sealed class AttachmentGuard(RecordAttachmentOptions options, IPermissionChecker permissions, IServiceProvider services)
{
    public static readonly Error NotFound = Error.NotFound("attachments.not_found", "There is no such record or attachment.");

    public static readonly Error Forbidden = Error.Forbidden("attachments.forbidden", "You may not change the attachments of this record.");

    public async Task<Result<RecordAttachmentType>> CheckAsync(AttachmentAccess access, CancellationToken cancellationToken)
    {
        if (options.Find(access.EntityType) is not { } type || !type.Slots.ContainsKey(access.Slot)
            || await type.VisibleIdsAsync(services, cancellationToken) is not { } visible
            || !await visible.AnyAsync(id => id == access.EntityId, cancellationToken)
            || !await PassesAsync(type, access with { Write = false }, cancellationToken))
        {
            return NotFound;
        }

        return !access.Write || await MayWriteAsync(type, access, cancellationToken) ? type : Forbidden;
    }

    /// <summary>For a record the user sees already.</summary>
    public async Task<bool> MayWriteAsync(RecordAttachmentType type, AttachmentAccess access, CancellationToken cancellationToken) =>
        (type.WritePermission is not { } permission || await permissions.IsGrantedAsync(permission, cancellationToken))
        && await PassesAsync(type, access with { Write = true }, cancellationToken);

    private async Task<bool> PassesAsync(RecordAttachmentType type, AttachmentAccess access, CancellationToken cancellationToken)
    {
        foreach (var check in type.Checks)
        {
            if (!await check(services, access, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }
}

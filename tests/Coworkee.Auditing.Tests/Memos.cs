using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Auditing.Tests;

public enum MemoPriority
{
    Low,
    High,
}

public sealed class Memo : AuditedAggregateRoot, IMultiTenant, ISoftDelete
{
    public required string Text { get; set; }

    public MemoPriority Priority { get; set; }

    [Sensitive]
    public string? Secret { get; set; }

    public Guid TenantId { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }
}

public sealed record SaveMemo(Guid Id, string Text, MemoPriority Priority) : ICommand<Result>;

internal sealed class SaveMemoHandler(CoworkeeDbContext db, IPermissionChecker permissions) : IHandler<SaveMemo, Result>
{
    public async Task<Result> HandleAsync(SaveMemo command, CancellationToken cancellationToken)
    {
        if (!await permissions.IsGrantedAsync(NotesModel.Manage, cancellationToken))
        {
            return Error.Forbidden("memo.forbidden", "No.");
        }

        if (command.Text == "locked")
        {
            return Error.Validation("text", "Locked memos cannot be saved.");
        }

        var memo = await db.Set<Memo>().SingleOrDefaultAsync(m => m.Id == command.Id, cancellationToken);
        if (memo is null)
        {
            return Error.NotFound("memo.not_found", "Gone.");
        }

        memo.Text = command.Text;
        memo.Priority = command.Priority;
        return Result.Success();
    }
}

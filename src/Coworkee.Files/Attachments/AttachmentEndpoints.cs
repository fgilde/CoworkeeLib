using Coworkee.Application;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.AspNetCore.RateLimiting;
using Coworkee.Contracts.Attachments;
using Coworkee.Contracts.Files;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Tokens;
using Coworkee.Core.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Coworkee.Files.Attachments;

internal static class AttachmentEndpoints
{
    public static void MapAttachmentEndpoints(this IEndpointRouteBuilder app, RecordAttachmentOptions options)
    {
        var group = app.MapCoworkeeApi("/" + AttachmentUrls.Root).WithTags("Attachments").RequireAuthorization();
        group.MapGet("/{entityType}/{entityId:guid}", ListAsync);
        group.MapGet("/{entityType}/{entityId:guid}/{slot}", (string entityType, Guid entityId, string slot, AttachmentGuard guard, IRecordAttachments attachments, CancellationToken ct) =>
            SlotAsync(new(entityType, entityId, slot, false), guard, attachments, ct).ToHttpResult());
        group.MapGet("/{entityType}/{entityId:guid}/{slot}/{id:guid}", ContentAsync);
        // the file viewer asks with HEAD for type and size first
        group.MapMethods("/{entityType}/{entityId:guid}/{slot}/{id:guid}", [HttpMethods.Head], ContentAsync).ExcludeFromDescription();
        var largest = options.Types.Values.SelectMany(t => t.Slots.Values).Select(s => s.MaxBytes).DefaultIfEmpty(RecordAttachmentType.DefaultMaxBytes).Max();
        group.MapPost("/{entityType}/{entityId:guid}/{slot}/{id:guid}/token", TokenAsync);
        group.MapGet("/token/{token}/{name}", TokenContentAsync).AllowAnonymous().RequireCoworkeeRateLimit(CoworkeeRateLimitOptions.Tokens);
        group.MapMethods("/token/{token}/{name}", [HttpMethods.Head], TokenContentAsync).AllowAnonymous().RequireCoworkeeRateLimit(CoworkeeRateLimitOptions.Tokens)
            .ExcludeFromDescription();
        group.MapPost("/{entityType}/{entityId:guid}/{slot}", UploadAsync).WithMetadata(new RequestSizeLimitAttribute(largest + 64 * 1024));
        group.MapDelete("/{entityType}/{entityId:guid}/{slot}/{id:guid}", DeleteAsync);
    }

    private static async Task<IResult> ListAsync(string entityType, Guid entityId, AttachmentGuard guard, IRecordAttachments attachments, RecordAttachmentOptions options, CancellationToken cancellationToken)
    {
        var slots = new List<AttachmentSlotDto>();
        foreach (var slot in options.Find(entityType)?.Slots.Keys ?? Enumerable.Empty<string>())
        {
            if (await SlotAsync(new(entityType, entityId, slot, false), guard, attachments, cancellationToken) is { IsSuccess: true } result)
            {
                slots.Add(result.Value);
            }
        }

        return slots.Count > 0 ? Results.Ok(slots) : AttachmentGuard.NotFound.ToProblem();
    }

    private static async Task<Result<AttachmentSlotDto>> SlotAsync(AttachmentAccess access, AttachmentGuard guard, IRecordAttachments attachments, CancellationToken cancellationToken)
    {
        var type = await guard.CheckAsync(access, cancellationToken);
        if (!type.IsSuccess)
        {
            return type.Error!;
        }

        var slot = type.Value.Slots[access.Slot];
        return new AttachmentSlotDto(slot.Name, slot.MaxBytes, slot.ContentTypes, slot.Multiple, await guard.MayWriteAsync(type.Value, access, cancellationToken),
            await attachments.ListAsync(access.EntityType, access.EntityId, access.Slot, cancellationToken));
    }

    private static async Task<IResult> ContentAsync(
        string entityType, Guid entityId, string slot, Guid id, bool? download, AttachmentGuard guard, IRecordAttachments attachments, CancellationToken cancellationToken)
    {
        if ((await guard.CheckAsync(new(entityType, entityId, slot, false), cancellationToken)).Error is { } error)
        {
            return error.ToProblem();
        }

        if (await attachments.OpenAsync(id, cancellationToken) is not { } file)
        {
            return AttachmentGuard.NotFound.ToProblem();
        }

        if (!Belongs(file.Attachment, entityType, entityId, slot))
        {
            await file.Content.DisposeAsync();
            return AttachmentGuard.NotFound.ToProblem();
        }

        return new BlobContentResult(file.Content, file.Attachment.ContentType, file.Attachment.FileName, download == true);
    }

    /// <summary>A short-lived anonymous link to one attachment (like a file token), for whoever may read the record.</summary>
    private static async Task<IResult> TokenAsync(
        string entityType, Guid entityId, string slot, Guid id, AttachmentGuard guard, IRecordAttachments attachments, IAccessTokens tokens, IUnitOfWork unitOfWork,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        if ((await guard.CheckAsync(new(entityType, entityId, slot, false), cancellationToken)).Error is { } error)
        {
            return error.ToProblem();
        }

        if ((await attachments.ListAsync(entityType, entityId, slot, cancellationToken)).FirstOrDefault(a => a.Id == id) is not { } attachment)
        {
            return AttachmentGuard.NotFound.ToProblem();
        }

        var token = tokens.Issue(TokenPurpose, attachment.Id, FileTokens.Lifetime, singleUse: false);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Results.Ok(new FileTokenDto(token, AttachmentUrls.Token(token, attachment.FileName), clock.GetUtcNow().Add(FileTokens.Lifetime)));
    }

    private static async Task<IResult> TokenContentAsync(string token, string name, bool? download, IAccessTokens tokens, IRecordAttachments attachments, CancellationToken cancellationToken)
    {
        if (await tokens.RedeemAsync(TokenPurpose, token, cancellationToken) is not { } grant)
        {
            return AttachmentGuard.NotFound.ToProblem();
        }

        using var tenant = CurrentUserScope.Begin(new ImpersonatedUser(null, grant.TenantId));
        return await attachments.OpenAsync(grant.TargetId, cancellationToken) is { } file
            ? new BlobContentResult(file.Content, file.Attachment.ContentType, file.Attachment.FileName, download == true)
            : AttachmentGuard.NotFound.ToProblem();
    }

    private const string TokenPurpose = "attachment";

    /// <summary>The body is the file itself, its Content-Type the file's type.</summary>
    private static async Task<IResult> UploadAsync(
        string entityType, Guid entityId, string slot, string name, HttpRequest request, AttachmentGuard guard, IRecordAttachments attachments, IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        if ((await guard.CheckAsync(new(entityType, entityId, slot, true), cancellationToken)).Error is { } error)
        {
            return error.ToProblem();
        }

        var result = await attachments.PutAsync(entityType, entityId, slot, name, request.ContentType, request.Body, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.Error!.ToProblem();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Results.Ok(result.Value);
    }

    private static async Task<IResult> DeleteAsync(
        string entityType, Guid entityId, string slot, Guid id, AttachmentGuard guard, IRecordAttachments attachments, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        if ((await guard.CheckAsync(new(entityType, entityId, slot, true), cancellationToken)).Error is { } error)
        {
            return error.ToProblem();
        }

        var existing = await attachments.ListAsync(entityType, entityId, slot, cancellationToken);
        if (!existing.Any(a => a.Id == id) || !await attachments.DeleteAsync(id, cancellationToken))
        {
            return AttachmentGuard.NotFound.ToProblem();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static bool Belongs(RecordAttachmentDto attachment, string entityType, Guid entityId, string slot) =>
        attachment.EntityType == entityType && attachment.EntityId == entityId && attachment.Slot == slot;
}

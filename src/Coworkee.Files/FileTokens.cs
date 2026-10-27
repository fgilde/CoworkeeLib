using Coworkee.Application.Messaging;
using Coworkee.Contracts.Files;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Infrastructure.Tokens;
using Coworkee.Storage;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Files;

/// <summary>A link that opens the file without sign-in for <see cref="FileTokens.Lifetime"/>, e.g. for a viewer or a native download; who may open the file may create it.</summary>
public sealed record CreateFileToken(Guid Id) : ICommand<Result<FileTokenDto>>;

/// <summary>The file behind a token of <see cref="CreateFileToken"/>, whichever organisation it belongs to.</summary>
public sealed record OpenFileByToken(string Token) : IQuery<Result<FileContent>>;

public static class FileTokens
{
    public const string Purpose = "file";

    /// <summary>The viewer asks several times (HEAD, ranges), so the token works until it expires.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
}

internal sealed class FileTokenHandlers(CoworkeeDbContext db, FolderAccess access, IAccessTokens tokens, IBlobStorage storage, TimeProvider clock)
    : IHandler<CreateFileToken, Result<FileTokenDto>>,
      IHandler<OpenFileByToken, Result<FileContent>>
{
    public async Task<Result<FileTokenDto>> HandleAsync(CreateFileToken command, CancellationToken cancellationToken)
    {
        var file = await db.Set<StoredFile>().AsNoTracking().SingleOrDefaultAsync(f => f.Id == command.Id, cancellationToken);
        if (file is null)
        {
            return FileErrors.FileNotFound;
        }

        if (!await access.CanAsync(FilePermissions.View, file.FolderId, cancellationToken))
        {
            return FileErrors.Forbidden;
        }

        var token = tokens.Issue(FileTokens.Purpose, file.Id, FileTokens.Lifetime, singleUse: false);
        return new FileTokenDto(token, FileLinks.Token(token, file.Name), clock.GetUtcNow().Add(FileTokens.Lifetime));
    }

    public async Task<Result<FileContent>> HandleAsync(OpenFileByToken query, CancellationToken cancellationToken)
    {
        if (await tokens.RedeemAsync(FileTokens.Purpose, query.Token, cancellationToken) is not { } grant)
        {
            return FileErrors.FileNotFound;
        }

        var file = await db.Set<StoredFile>().IgnoreQueryFilters([CoworkeeDbContext.TenantFilter]).AsNoTracking()
            .SingleOrDefaultAsync(f => f.Id == grant.TargetId && f.TenantId == grant.TenantId, cancellationToken);
        return file is not null && await storage.OpenReadAsync(file.BlobKey, cancellationToken) is { } content
            ? new FileContent(file.Name, file.ContentType, content)
            : FileErrors.FileNotFound;
    }
}

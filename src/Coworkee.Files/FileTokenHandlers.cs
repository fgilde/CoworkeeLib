using Coworkee.Application.Messaging;
using Coworkee.Contracts.Files;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Infrastructure.Tokens;
using Coworkee.Storage;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Files;

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

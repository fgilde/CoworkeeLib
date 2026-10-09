using Coworkee.Application.Registration;
using Coworkee.Contracts.Files;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Storage;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Files;

/// <summary>
/// Puts registration documents into Registrations/{user} in the files. The folder carries no grant but one for the user (through
/// the role <see cref="ReaderRole"/>, which may view files); global file grants do not reach the tree (<see cref="RegistrationFolders"/>),
/// so only the user and holders of Files.Registrations.View (admins) see it.
/// </summary>
internal sealed class FileRegistrationDocuments(CoworkeeDbContext db, ICurrentUser currentUser, IBlobStorage storage, TimeProvider clock) : IRegistrationDocumentStore
{
    public const string RootFolder = "Registrations";
    public const string ReaderRole = "Registration documents";

    public async Task SaveAsync(RegistrationDocument document, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId ?? throw new InvalidOperationException("Registration documents need a tenant.");
        var root = await FolderAsync(RootFolder, null, cancellationToken);
        var folder = await FolderAsync(FileNames.Clean(document.UserName) ?? document.UserId.ToString(), root.Id, cancellationToken);
        await GrantAsync(folder.Id, document.UserId, tenantId, cancellationToken);

        var key = BlobKeys.New(tenantId, clock);
        await storage.PutAsync(key, document.Content, document.ContentType, cancellationToken);
        db.Add(new StoredFile
        {
            Name = FileNames.Clean($"{document.Slot.Name} - {document.FileName}") ?? document.FileName,
            FolderId = folder.Id,
            ContentType = document.ContentType,
            Size = document.Size,
            BlobKey = key,
            CreatedAt = clock.GetUtcNow(),
            CreatedBy = document.UserId,
        });
    }

    private async Task<FileFolder> FolderAsync(string name, Guid? parentId, CancellationToken cancellationToken)
    {
        var folder = db.Set<FileFolder>().Local.FirstOrDefault(f => f.Name == name && f.ParentId == parentId)
            ?? await db.Set<FileFolder>().FirstOrDefaultAsync(f => f.Name == name && f.ParentId == parentId, cancellationToken);
        if (folder is null)
        {
            folder = new FileFolder { Name = name, ParentId = parentId, CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId };
            db.Add(folder);
        }

        return folder;
    }

    private async Task GrantAsync(Guid folderId, Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        var role = db.Set<Role>().Local.FirstOrDefault(r => r.Name == ReaderRole && r.TenantId == tenantId)
            ?? await db.Set<Role>().FirstOrDefaultAsync(r => r.Name == ReaderRole && r.TenantId == tenantId, cancellationToken);
        if (role is null)
        {
            role = new Role
            {
                Name = ReaderRole, NormalizedName = ReaderRole.ToUpperInvariant(), TenantId = tenantId,
                Description = "Lets users see the documents they uploaded when they registered; granted on their folder only.",
            };
            db.Add(role);
            db.Add(new PermissionGrant { TenantId = tenantId, Name = FilePermissions.View, ProviderType = PermissionProviderType.Role, ProviderKey = role.Id });
        }

        if (!db.Set<ResourcePermission>().Local.Any(p => p.ResourceId == folderId && p.PrincipalId == userId)
            && !await db.Set<ResourcePermission>().AnyAsync(p => p.ResourceType == FilePermissions.FolderResource && p.ResourceId == folderId && p.PrincipalId == userId, cancellationToken))
        {
            db.Add(new ResourcePermission
            {
                ResourceType = FilePermissions.FolderResource, ResourceId = folderId, PrincipalType = PrincipalType.User, PrincipalId = userId, RoleId = role.Id, TenantId = tenantId,
            });
        }
    }
}

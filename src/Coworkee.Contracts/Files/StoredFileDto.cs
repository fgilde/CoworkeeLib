namespace Coworkee.Contracts.Files;

/// <summary>A stored file; <see cref="IsPublic"/> files are served to anyone under <see cref="FileLinks.Public"/>.</summary>
public sealed record StoredFileDto(Guid Id, Guid? FolderId, string Name, string ContentType, long Size, DateTimeOffset CreatedAt, Guid? CreatedBy, bool IsPublic = false);

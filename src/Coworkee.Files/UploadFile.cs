using Coworkee.Application.Messaging;
using Coworkee.Contracts.Files;
using Coworkee.Core.Results;

namespace Coworkee.Files;

/// <summary>Streams <paramref name="Content"/> into the blob storage; read only after the permission check.</summary>
public sealed record UploadFile(Guid? FolderId, string Name, string? ContentType, Stream Content) : ICommand<Result<StoredFileDto>>;

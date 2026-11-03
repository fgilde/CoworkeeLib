using Coworkee.Application.Messaging;
using Coworkee.Contracts.Files;
using Coworkee.Core.Results;

namespace Coworkee.Files;

public sealed record GetFolderContent(Guid? FolderId) : IQuery<Result<FolderContentDto>>;

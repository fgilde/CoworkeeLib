using Coworkee.Application.Messaging;
using Coworkee.Contracts.Files;
using Coworkee.Core.Results;

namespace Coworkee.Files;

public sealed record CreateFolder(CreateFolderRequest Folder) : ICommand<Result<FolderDto>>;

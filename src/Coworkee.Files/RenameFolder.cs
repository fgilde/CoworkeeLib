using Coworkee.Application.Messaging;
using Coworkee.Contracts.Files;
using Coworkee.Core.Results;

namespace Coworkee.Files;

public sealed record RenameFolder(Guid Id, string Name) : ICommand<Result<FolderDto>>;

using Coworkee.Application.Messaging;
using Coworkee.Contracts.Files;
using Coworkee.Core.Results;

namespace Coworkee.Files;

public sealed record RenameFile(Guid Id, string Name) : ICommand<Result<StoredFileDto>>;

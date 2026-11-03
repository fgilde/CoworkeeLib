using Coworkee.Application.Messaging;
using Coworkee.Contracts.Files;
using Coworkee.Core.Results;

namespace Coworkee.Files;

public sealed record GetFile(Guid Id) : IQuery<Result<StoredFileDto>>;

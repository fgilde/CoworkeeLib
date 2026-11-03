using Coworkee.Application.Messaging;
using Coworkee.Core.Results;

namespace Coworkee.Files;

public sealed record OpenFile(Guid Id) : IQuery<Result<FileContent>>;

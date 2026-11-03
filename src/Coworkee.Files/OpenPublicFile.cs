using Coworkee.Application.Messaging;
using Coworkee.Core.Results;

namespace Coworkee.Files;

/// <summary>A public file for anyone, whichever organisation it belongs to.</summary>
public sealed record OpenPublicFile(Guid Id) : IQuery<Result<FileContent>>;

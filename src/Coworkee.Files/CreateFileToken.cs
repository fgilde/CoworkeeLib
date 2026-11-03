using Coworkee.Application.Messaging;
using Coworkee.Contracts.Files;
using Coworkee.Core.Results;

namespace Coworkee.Files;

/// <summary>A link that opens the file without sign-in for <see cref="FileTokens.Lifetime"/>, e.g. for a viewer or a native download; who may open the file may create it.</summary>
public sealed record CreateFileToken(Guid Id) : ICommand<Result<FileTokenDto>>;

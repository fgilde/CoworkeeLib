using Coworkee.Application.Messaging;
using Coworkee.Core.Results;

namespace Coworkee.Files;

/// <summary>The file behind a token of <see cref="CreateFileToken"/>, whichever organisation it belongs to.</summary>
public sealed record OpenFileByToken(string Token) : IQuery<Result<FileContent>>;

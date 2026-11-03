using Coworkee.Application.Messaging;
using Coworkee.Contracts.Theming;
using Coworkee.Core.Results;

namespace Coworkee.Theming;

/// <summary>A theme every member of the signed-in user's organisation may choose (built-in or published), whoever asks; not found otherwise.</summary>
public sealed record GetChoosableTheme(Guid Id) : IQuery<Result<ThemeDto>>;

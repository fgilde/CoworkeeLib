using Coworkee.Application.Messaging;
using Coworkee.Contracts.Theming;
using Coworkee.Core.Results;

namespace Coworkee.Theming;

/// <summary>
/// For anyone, the auth server's sign-in page: the themes of the system organisation a visitor may have used (built-in and published ones, with the
/// organisation's look of them), the default marked <see cref="ThemeDto.IsDefault"/>.
/// </summary>
public sealed record GetSignInThemes : IQuery<Result<IReadOnlyList<ThemeDto>>>;

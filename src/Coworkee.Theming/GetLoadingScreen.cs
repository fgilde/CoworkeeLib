using Coworkee.Application.Messaging;
using Coworkee.Contracts.Theming;
using Coworkee.Core.Results;

namespace Coworkee.Theming;

/// <summary>
/// For anyone: the web app renders its loading screen from it before the app (and the sign-in) runs. <see cref="ThemeId"/>, the theme a signed-in
/// user chose, wins when it is one every member of the user's organisation may choose; anonymous callers always get the default.
/// </summary>
public sealed record GetLoadingScreen(Guid? ThemeId = null) : IQuery<Result<LoadingScreenDto>>;

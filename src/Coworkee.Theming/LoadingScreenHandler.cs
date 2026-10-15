using Coworkee.Application.Messaging;
using Coworkee.Contracts.Theming;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Microsoft.Extensions.Options;

namespace Coworkee.Theming;

/// <summary>
/// For anyone: the web app renders its loading screen from it before the app (and the sign-in) runs. <see cref="ThemeId"/>, the theme a signed-in
/// user chose, wins when it is one every member of the user's organisation may choose; anonymous callers always get the default.
/// </summary>
public sealed record GetLoadingScreen(Guid? ThemeId = null) : IQuery<Result<LoadingScreenDto>>;

internal sealed class LoadingScreenHandler(IDispatcher dispatcher, IOptionsMonitor<SignInDesign> design, ICurrentUser currentUser) : IHandler<GetLoadingScreen, Result<LoadingScreenDto>>
{
    public async Task<Result<LoadingScreenDto>> HandleAsync(GetLoadingScreen query, CancellationToken cancellationToken)
    {
        var chosen = query.ThemeId is { } id ? await dispatcher.SendAsync(new GetChoosableTheme(id), cancellationToken) : null;
        var theme = chosen is { IsSuccess: true } ? chosen : await dispatcher.SendAsync(new GetCurrentTheme(), cancellationToken);
        var current = design.CurrentValue;
        return new LoadingScreenDto(theme.IsSuccess ? theme.Value : null, current.LoadingScreen ?? LoadingScreenBackground.Animated, current.MediaUrl, currentUser.IsAuthenticated);
    }
}

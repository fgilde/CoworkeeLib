using Coworkee.Application.Messaging;
using Coworkee.Contracts.Theming;
using Coworkee.Core.Results;
using Microsoft.Extensions.Options;

namespace Coworkee.Theming;

/// <summary>For anyone: the web app renders its loading screen from it before the app (and the sign-in) runs.</summary>
public sealed record GetLoadingScreen : IQuery<Result<LoadingScreenDto>>;

internal sealed class LoadingScreenHandler(IDispatcher dispatcher, IOptionsMonitor<SignInDesign> design) : IHandler<GetLoadingScreen, Result<LoadingScreenDto>>
{
    public async Task<Result<LoadingScreenDto>> HandleAsync(GetLoadingScreen query, CancellationToken cancellationToken)
    {
        var theme = await dispatcher.SendAsync(new GetCurrentTheme(), cancellationToken);
        var current = design.CurrentValue;
        return new LoadingScreenDto(theme.IsSuccess ? theme.Value : null, current.LoadingScreen ?? LoadingScreenBackground.Animated, current.MediaUrl);
    }
}

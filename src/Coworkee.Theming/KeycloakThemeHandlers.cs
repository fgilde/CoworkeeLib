using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Theming;
using Coworkee.Core.Results;

namespace Coworkee.Theming;

[RequiresPermission(ThemePermissions.Manage)]
public sealed record GetKeycloakThemeStatus : IQuery<Result<KeycloakThemeStatusDto>>;

/// <summary>Writes the Keycloak login theme now, e.g. after Keycloak's theme folder was emptied.</summary>
[RequiresPermission(ThemePermissions.Manage)]
public sealed record ApplyKeycloakTheme : ICommand<Result<KeycloakThemeStatusDto>>;

internal sealed class KeycloakThemeHandlers(KeycloakThemeSync sync)
    : IHandler<GetKeycloakThemeStatus, Result<KeycloakThemeStatusDto>>,
      IHandler<ApplyKeycloakTheme, Result<KeycloakThemeStatusDto>>
{
    public Task<Result<KeycloakThemeStatusDto>> HandleAsync(GetKeycloakThemeStatus query, CancellationToken cancellationToken) =>
        Task.FromResult(Result<KeycloakThemeStatusDto>.Success(sync.Status));

    public async Task<Result<KeycloakThemeStatusDto>> HandleAsync(ApplyKeycloakTheme command, CancellationToken cancellationToken) =>
        await sync.WriteAsync(cancellationToken);
}

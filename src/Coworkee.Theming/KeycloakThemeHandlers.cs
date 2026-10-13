using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Theming;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Theming;

[RequiresPermission(ThemePermissions.Manage)]
public sealed record GetKeycloakThemeStatus : IQuery<Result<KeycloakThemeStatusDto>>;

/// <summary>Writes the Keycloak login theme now, e.g. after Keycloak's theme folder was emptied.</summary>
[RequiresPermission(ThemePermissions.Manage)]
public sealed record ApplyKeycloakTheme : ICommand<Result<KeycloakThemeStatusDto>>;

/// <summary>Keycloak and its theme folder belong to the installation, so only the system organisation sees and changes them.</summary>
internal sealed class KeycloakThemeHandlers(KeycloakThemeSync sync, ICurrentUser currentUser, IServiceProvider services)
    : IHandler<GetKeycloakThemeStatus, Result<KeycloakThemeStatusDto>>,
      IHandler<ApplyKeycloakTheme, Result<KeycloakThemeStatusDto>>
{
    private static readonly Error HostOnly = Error.Forbidden("themes.keycloak_host_only", "The Keycloak sign-in page is managed from the system organisation.");

    public async Task<Result<KeycloakThemeStatusDto>> HandleAsync(GetKeycloakThemeStatus query, CancellationToken cancellationToken) =>
        await IsSystemAsync(cancellationToken) ? sync.Status : new KeycloakThemeStatusDto(false, null, null, null);

    public async Task<Result<KeycloakThemeStatusDto>> HandleAsync(ApplyKeycloakTheme command, CancellationToken cancellationToken) =>
        await IsSystemAsync(cancellationToken) ? await sync.WriteAsync(cancellationToken) : HostOnly;

    private async Task<bool> IsSystemAsync(CancellationToken cancellationToken) =>
        currentUser.TenantId is { } tenantId && services.GetService<ITenantDirectory>() is { } tenants && await tenants.IsSystemTenantAsync(tenantId, cancellationToken);
}

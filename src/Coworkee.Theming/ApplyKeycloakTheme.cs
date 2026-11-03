using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Theming;
using Coworkee.Core.Results;

namespace Coworkee.Theming;

/// <summary>Writes the Keycloak login theme now, e.g. after Keycloak's theme folder was emptied.</summary>
[RequiresPermission(ThemePermissions.Manage)]
public sealed record ApplyKeycloakTheme : ICommand<Result<KeycloakThemeStatusDto>>;

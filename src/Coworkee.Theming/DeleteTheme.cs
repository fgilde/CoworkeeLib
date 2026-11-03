using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Theming;
using Coworkee.Core.Results;

namespace Coworkee.Theming;

[RequiresPermission(ThemePermissions.Manage)]
public sealed record DeleteTheme(Guid Id) : ICommand<Result>;

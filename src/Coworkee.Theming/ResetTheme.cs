using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Theming;
using Coworkee.Core.Results;

namespace Coworkee.Theming;

/// <summary>Gives a built-in theme its original look back in the organisation; whether users may choose it stays.</summary>
[RequiresPermission(ThemePermissions.Manage)]
public sealed record ResetTheme(Guid Id) : ICommand<Result>;

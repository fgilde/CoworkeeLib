using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Theming;
using Coworkee.Core.Results;

namespace Coworkee.Theming;

/// <summary>Whether the users of the organisation may choose the theme, a built-in one too; its look stays as it is.</summary>
[RequiresPermission(ThemePermissions.Manage)]
public sealed record SetThemePublished(Guid Id, bool IsPublished) : ICommand<Result>;

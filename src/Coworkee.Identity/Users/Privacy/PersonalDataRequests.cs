using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using FluentValidation;

namespace Coworkee.Identity.Users.Privacy;

/// <summary>Everything the modules store about the signed-in user, one section per module.</summary>
[AiTool(Exclude = true)]
public sealed record ExportMyPersonalData : IQuery<Result<IReadOnlyDictionary<string, object>>>;

/// <summary>Erases the signed-in user's data and account; the e-mail address confirms it.</summary>
[AiTool(Exclude = true)]
public sealed record DeleteMyAccount(string Email) : ICommand<Result>;

[AiTool(Exclude = true)]
[Application.Authorization.RequiresPermission(IdentityPermissions.Users.Manage)]
public sealed record DeleteUser(Guid Id) : ICommand<Result>;

internal sealed class DeleteMyAccountValidator : AbstractValidator<DeleteMyAccount>
{
    public DeleteMyAccountValidator() => RuleFor(c => c.Email).NotEmpty();
}

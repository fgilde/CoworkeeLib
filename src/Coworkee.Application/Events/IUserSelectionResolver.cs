using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using FluentValidation;

namespace Coworkee.Application.Events;

/// <summary>
/// Resolves a <see cref="UserSelection"/> on the server with the rights of the current user, as the Users set shows them (Coworkee.Identity implements it).
/// As audience of <see cref="EventTarget.Users(UserSelection)"/> it reaches the active users of the selection.
/// </summary>
public interface IUserSelectionResolver : IAudienceResolver
{
    /// <summary>The ids of the selection; Forbidden without the right to view users, Validation for a filter the user may not run.</summary>
    Task<Result<IReadOnlyCollection<Guid>>> ResolveAsync(UserSelection selection, CancellationToken cancellationToken);

    async Task<IReadOnlyCollection<Guid>> IAudienceResolver.ResolveAsync(object? args, CancellationToken cancellationToken)
    {
        var resolved = await ResolveAsync((UserSelection)args!, cancellationToken);
        return resolved.IsSuccess ? resolved.Value : throw new InvalidOperationException($"The user selection cannot be resolved: {resolved.Error!.Message}");
    }
}

/// <summary>Bounds of a <see cref="UserSelection"/> in a request.</summary>
public sealed class UserSelectionValidator : AbstractValidator<UserSelection>
{
    public UserSelectionValidator()
    {
        RuleFor(s => s.Filter).MaximumLength(4000);
        RuleFor(s => s.Included).Must(ids => ids is null || ids.Count <= UserSelection.MaxIds).WithMessage($"Add at most {UserSelection.MaxIds} users by hand; use a filter for more.");
        RuleFor(s => s.Excluded).Must(ids => ids is null || ids.Count <= UserSelection.MaxIds).WithMessage($"Leave out at most {UserSelection.MaxIds} users by hand; narrow the filter instead.");
        RuleFor(s => s).Must(s => !s.IsEmpty).WithMessage("Choose at least one user.").OverridePropertyName("Recipients");
    }
}

using FluentValidation;

namespace Coworkee.Identity.Groups;

// ponytail: the path is only resolved by the handler, so a dry run accepts rows whose parent an earlier row of the same file creates and cannot report an unknown path;
// names containing '/' cannot be addressed by path (rejected here), empty path segments are ignored, a blank description does not clear an existing one
internal sealed class ImportGroupValidator : AbstractValidator<ImportGroup>
{
    public ImportGroupValidator() => RuleFor(c => c.Row.Name).Cascade(CascadeMode.Stop).NotEmpty().MaximumLength(200).Must(name => !name.Contains('/')).WithMessage("A group name cannot contain '/'.");
}

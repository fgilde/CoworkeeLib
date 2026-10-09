using System.Globalization;
using Coworkee.Application.Localization;
using Microsoft.AspNetCore.Identity;

namespace Coworkee.Identity.Users;

/// <summary>The messages of ASP.NET Core Identity in the request language, through the app's texts (the English message is the key).</summary>
public sealed class LocalizedIdentityErrorDescriber(IEnumerable<ITextTranslator> translators) : IdentityErrorDescriber
{
    public override IdentityError DefaultError() => Error(nameof(DefaultError), "An unknown failure has occurred.");

    public override IdentityError ConcurrencyFailure() => Error(nameof(ConcurrencyFailure), "Optimistic concurrency failure, object has been modified.");

    public override IdentityError PasswordMismatch() => Error(nameof(PasswordMismatch), "Incorrect password.");

    public override IdentityError InvalidToken() => Error(nameof(InvalidToken), "Invalid token.");

    public override IdentityError RecoveryCodeRedemptionFailed() => Error(nameof(RecoveryCodeRedemptionFailed), "Recovery code redemption failed.");

    public override IdentityError LoginAlreadyAssociated() => Error(nameof(LoginAlreadyAssociated), "A user with this login already exists.");

    public override IdentityError InvalidUserName(string? userName) => Error(nameof(InvalidUserName), "Username '{0}' is invalid, can only contain letters or digits.", userName);

    public override IdentityError InvalidEmail(string? email) => Error(nameof(InvalidEmail), "Email '{0}' is invalid.", email);

    public override IdentityError DuplicateUserName(string userName) => Error(nameof(DuplicateUserName), "Username '{0}' is already taken.", userName);

    public override IdentityError DuplicateEmail(string email) => Error(nameof(DuplicateEmail), "Email '{0}' is already taken.", email);

    public override IdentityError InvalidRoleName(string? role) => Error(nameof(InvalidRoleName), "Role name '{0}' is invalid.", role);

    public override IdentityError DuplicateRoleName(string role) => Error(nameof(DuplicateRoleName), "Role name '{0}' is already taken.", role);

    public override IdentityError UserAlreadyHasPassword() => Error(nameof(UserAlreadyHasPassword), "User already has a password set.");

    public override IdentityError UserLockoutNotEnabled() => Error(nameof(UserLockoutNotEnabled), "Lockout is not enabled for this user.");

    public override IdentityError UserAlreadyInRole(string role) => Error(nameof(UserAlreadyInRole), "User already in role '{0}'.", role);

    public override IdentityError UserNotInRole(string role) => Error(nameof(UserNotInRole), "User is not in role '{0}'.", role);

    public override IdentityError PasswordTooShort(int length) => Error(nameof(PasswordTooShort), "Passwords must be at least {0} characters.", length);

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        Error(nameof(PasswordRequiresUniqueChars), "Passwords must use at least {0} different characters.", uniqueChars);

    public override IdentityError PasswordRequiresNonAlphanumeric() =>
        Error(nameof(PasswordRequiresNonAlphanumeric), "Passwords must have at least one non alphanumeric character.");

    public override IdentityError PasswordRequiresDigit() => Error(nameof(PasswordRequiresDigit), "Passwords must have at least one digit ('0'-'9').");

    public override IdentityError PasswordRequiresLower() => Error(nameof(PasswordRequiresLower), "Passwords must have at least one lowercase ('a'-'z').");

    public override IdentityError PasswordRequiresUpper() => Error(nameof(PasswordRequiresUpper), "Passwords must have at least one uppercase ('A'-'Z').");

    public IdentityError PasswordReused() => Error(nameof(PasswordReused), "Choose a password you did not use recently.");

    private IdentityError Error(string code, string text, params object?[] arguments) => new()
    {
        Code = code,
        Description = string.Format(CultureInfo.CurrentCulture, translators.Translate(text, CultureInfo.CurrentUICulture), arguments),
    };
}

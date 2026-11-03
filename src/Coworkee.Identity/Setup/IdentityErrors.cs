using Coworkee.Core.Results;
using Microsoft.AspNetCore.Identity;

namespace Coworkee.Identity.Setup;

public static class IdentityErrors
{
    public static Error ToError(IdentityResult result) =>
        Error.Validation(result.Errors
            .GroupBy(e => e.Code.Contains("Password", StringComparison.Ordinal) ? "Password"
                : e.Code.Contains("Email", StringComparison.Ordinal) || e.Code.Contains("UserName", StringComparison.Ordinal) ? "Email"
                : "User")
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray(), StringComparer.Ordinal));
}

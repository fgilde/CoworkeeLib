using Coworkee.Account;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coworkee.AuthServer.Pages.Account;

public sealed class ResetPasswordModel(UserManager<User> users, CoworkeeDbContext db) : PageModel
{
    private const string InvalidLink = "The link is invalid or has expired. Request a new one.";

    [BindProperty(SupportsGet = true)]
    public Guid UserId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Code { get; set; }

    [BindProperty]
    public PasswordInput Input { get; set; } = new();

    public bool Done { get; private set; }

    public List<string> Errors { get; } = [];

    public async Task OnPostAsync()
    {
        if (Input.Password != Input.ConfirmPassword)
        {
            Errors.Add("The passwords do not match.");
            return;
        }

        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        var user = await users.FindByIdAsync(UserId.ToString());
        var token = AccountTokens.Decode(Code);
        if (user is null || token is null || !user.IsActive)
        {
            Errors.Add(InvalidLink);
            return;
        }

        var result = await users.ResetPasswordAsync(user, token, Input.Password);
        if (!result.Succeeded)
        {
            Errors.AddRange(result.Errors.Select(e => e.Code == "InvalidToken" ? InvalidLink : e.Description));
            return;
        }

        await users.ResetAccessFailedCountAsync(user);
        await users.SetLockoutEndDateAsync(user, null);
        await db.SaveChangesAsync(HttpContext.RequestAborted);
        Done = true;
    }

    public sealed class PasswordInput
    {
        public string Password { get; set; } = string.Empty;

        public string ConfirmPassword { get; set; } = string.Empty;
    }
}

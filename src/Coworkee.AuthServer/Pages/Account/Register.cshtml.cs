using Coworkee.Account;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Settings;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coworkee.AuthServer.Pages.Account;

public sealed class RegisterModel(
    UserManager<User> users, IAccountMailer mailer, CoworkeeDbContext db, ISettingProvider settings, ITenantDirectory tenants) : PageModel
{
    [BindProperty]
    public RegisterInput Input { get; set; } = new();

    public bool Registered { get; private set; }

    public List<string> Errors { get; } = [];

    public async Task<IActionResult> OnGetAsync() => await AllowedAsync() ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await AllowedAsync() || await tenants.GetSystemTenantIdAsync(HttpContext.RequestAborted) is not { } tenantId)
        {
            return NotFound();
        }

        if (Input.Password != Input.ConfirmPassword)
        {
            Errors.Add("The passwords do not match.");
            return Page();
        }

        var user = new User
        {
            TenantId = tenantId,
            UserName = Input.Email.Trim(),
            Email = Input.Email.Trim(),
            FirstName = string.IsNullOrWhiteSpace(Input.FirstName) ? null : Input.FirstName.Trim(),
            LastName = string.IsNullOrWhiteSpace(Input.LastName) ? null : Input.LastName.Trim(),
            IsActive = false,
        };
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, tenantId));
        var created = await users.CreateAsync(user, Input.Password);
        if (!created.Succeeded)
        {
            Errors.AddRange(created.Errors.Select(e => e.Code is "DuplicateEmail" or "DuplicateUserName"
                ? "This address cannot be used. If it is yours, try signing in or resetting the password."
                : e.Description));
            return Page();
        }

        await mailer.SendEmailConfirmationAsync(user, HttpContext.RequestAborted);
        await db.SaveChangesAsync(HttpContext.RequestAborted);
        Registered = true;
        return Page();
    }

    private Task<bool> AllowedAsync() => settings.GetAsync<bool>(AccountSettings.AllowRegistration, HttpContext.RequestAborted);

    public sealed class RegisterInput
    {
        public string Email { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;

        public string ConfirmPassword { get; set; } = string.Empty;

        public string? FirstName { get; set; }

        public string? LastName { get; set; }
    }
}

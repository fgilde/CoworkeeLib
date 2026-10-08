using Coworkee.Account;
using Coworkee.Contracts.Configuration;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Settings;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Coworkee.AuthServer.Pages.Account;

public sealed class LoginModel(
    SignInManager<User> signIn, UserManager<User> users, CoworkeeDbContext db, ISettingProvider settings, IOptions<AuthServerOptions> options) : PageModel
{
    [BindProperty]
    public LoginInput Input { get; set; } = new();

    public string? ErrorMessage { get; private set; }

    public bool AllowRegistration { get; private set; }

    public LoginMode Mode => Providers.Count == 0 ? LoginMode.Internal : options.Value.External.Mode;

    public bool ShowsPasswordForm => Mode != LoginMode.External;

    public IReadOnlyList<(string Scheme, string DisplayName)> Providers =>
        [.. options.Value.External.Providers.Select(p => (p.Key, p.Value.DisplayName ?? p.Key))];

    public IActionResult OnPostExternal(string provider, string? returnUrl)
    {
        if (Mode == LoginMode.Internal || !options.Value.External.Providers.ContainsKey(provider))
        {
            return NotFound();
        }

        var callback = Url.Page("./ExternalLogin", "Callback", new { returnUrl });
        return new ChallengeResult(provider, signIn.ConfigureExternalAuthenticationProperties(provider, callback));
    }

    public async Task OnGetAsync(string? returnUrl)
    {
        AllowRegistration = await settings.GetAsync<bool>(AccountSettings.AllowRegistration);

        // the authorization request carries the client's login_hint (the address entered in setup, for example)
        if (returnUrl is not null && returnUrl.IndexOf('?') is var query and >= 0
            && Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(returnUrl[query..]).TryGetValue("login_hint", out var hint) && hint.ToString() is { Length: > 0 and <= 256 } email)
        {
            Input.Email = email;
        }
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        if (!ShowsPasswordForm)
        {
            return NotFound();
        }

        AllowRegistration = await settings.GetAsync<bool>(AccountSettings.AllowRegistration);
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        var user = await users.FindByEmailAsync(Input.Email);
        if (user is null || !user.IsActive)
        {
            ErrorMessage = "Email or password is not correct.";
            return Page();
        }

        var result = await signIn.PasswordSignInAsync(user, Input.Password, Input.RememberMe, lockoutOnFailure: true);
        await db.SaveChangesAsync();
        if (result.RequiresTwoFactor)
        {
            return RedirectToPage("LoginWith2fa", new { ReturnUrl = returnUrl, Input.RememberMe });
        }

        if (result.IsLockedOut)
        {
            ErrorMessage = "Too many attempts. Try again later.";
            return Page();
        }

        if (!result.Succeeded)
        {
            ErrorMessage = "Email or password is not correct.";
            return Page();
        }

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }

    public sealed class LoginInput
    {
        public string Email { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
    }
}

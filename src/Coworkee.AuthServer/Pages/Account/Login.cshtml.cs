using Coworkee.AuthServer.Registration;
using Coworkee.Contracts.Configuration;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Settings;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using static Coworkee.AuthServer.AuthTexts;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace Coworkee.AuthServer.Pages.Account;

public sealed class LoginModel(
    SignInManager<User> signIn, UserManager<User> users, CoworkeeDbContext db, ISettingProvider settings,
    ISettingDefinitionManager definitions, IOptions<AuthServerOptions> options) : PageModel
{
    [BindProperty]
    public LoginInput Input { get; set; } = new();

    public string? ErrorMessage { get; private set; }

    public bool AllowRegistration { get; private set; }

    public LoginMode Mode { get; private set; } = LoginMode.Internal;

    public bool ShowsPasswordForm => Mode != LoginMode.External;

    public bool ShowsPasskeys { get; private set; }

    public bool AllowUserName => options.Value.Login.AllowUserName;

    public IReadOnlyList<(string Scheme, string DisplayName)> Providers =>
        [.. options.Value.External.Providers.Select(p => (p.Key, p.Value.DisplayName ?? p.Key))];

    public async Task<IActionResult> OnPostExternalAsync(string provider, string? returnUrl)
    {
        Mode = await LoginModes.ResolveAsync(settings, definitions, options.Value, HttpContext.RequestAborted);
        if (Mode == LoginMode.Internal || !options.Value.External.Providers.ContainsKey(provider))
        {
            return NotFound();
        }

        var callback = Url.Page("./ExternalLogin", "Callback", new { returnUrl });
        return new ChallengeResult(provider, signIn.ConfigureExternalAuthenticationProperties(provider, callback));
    }

    public async Task OnGetAsync(string? returnUrl)
    {
        Mode = await LoginModes.ResolveAsync(settings, definitions, options.Value, HttpContext.RequestAborted);
        AllowRegistration = (await RegistrationPolicy.LoadAsync(settings, HttpContext.RequestAborted)).Enabled && ShowsPasswordForm;
        ShowsPasskeys = await Passkeys.EnabledAsync(Mode, settings, HttpContext.RequestAborted);

        // the authorization request carries the client's login_hint (the address entered in setup, for example)
        if (returnUrl is not null && returnUrl.IndexOf('?') is var query and >= 0
            && Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(returnUrl[query..]).TryGetValue("login_hint", out var hint) && hint.ToString() is { Length: > 0 and <= 256 } email)
        {
            Input.Email = email;
        }
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        if (!await PrepareAsync())
        {
            return NotFound();
        }

        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        var user = await users.FindByEmailAsync(Input.Email) ?? (AllowUserName ? await users.FindByNameAsync(Input.Email) : null);
        if (user is null || !MaySignIn(user))
        {
            ErrorMessage = T("Email or password is not correct.");
            return Page();
        }

        var result = await signIn.PasswordSignInAsync(user, Input.Password, Input.RememberMe, lockoutOnFailure: true);
        return await FinishAsync(user, result, returnUrl, "Email or password is not correct.");
    }

    public async Task<IActionResult> OnPostPasskeyOptionsAsync()
    {
        if (!await PrepareAsync() || !ShowsPasskeys)
        {
            return NotFound();
        }

        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        return Content(await signIn.MakePasskeyRequestOptionsAsync(null), "application/json");
    }

    /// <summary>A passkey is a strong sign-in on its own, so no second step follows; otherwise the same checks as the password.</summary>
    public async Task<IActionResult> OnPostPasskeyAsync(string? credential, string? returnUrl)
    {
        if (!await PrepareAsync() || !ShowsPasskeys)
        {
            return NotFound();
        }

        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        var assertion = string.IsNullOrEmpty(credential) ? null : await AssertAsync(credential);
        if (assertion is not { Succeeded: true, User: { } user, Passkey: { } passkey } || !MaySignIn(user))
        {
            ErrorMessage = T("The passkey was not accepted.");
            return Page();
        }

        var result = !await signIn.CanSignInAsync(user) ? SignInResult.NotAllowed
            : await users.IsLockedOutAsync(user) ? SignInResult.LockedOut
            : !(await users.AddOrUpdatePasskeyAsync(user, passkey)).Succeeded ? SignInResult.Failed
            : await SignInAsync(user);
        return await FinishAsync(user, result, returnUrl, "The passkey was not accepted.");
    }

    private async Task<bool> PrepareAsync()
    {
        Mode = await LoginModes.ResolveAsync(settings, definitions, options.Value, HttpContext.RequestAborted);
        AllowRegistration = (await RegistrationPolicy.LoadAsync(settings, HttpContext.RequestAborted)).Enabled;
        ShowsPasskeys = await Passkeys.EnabledAsync(Mode, settings, HttpContext.RequestAborted);
        return ShowsPasswordForm;
    }

    private bool MaySignIn(User user) => user.IsActive && Wildcards.Allows(options.Value.Login.AllowedEmails, user.Email);

    private async Task<PasskeyAssertionResult<User>?> AssertAsync(string credential)
    {
        try
        {
            return await signIn.PerformPasskeyAssertionAsync(credential);
        }
        catch (InvalidOperationException)
        {
            // no sign-in underway in this browser, e.g. the options came from another tab that started a newer one
            return null;
        }
    }

    private async Task<SignInResult> SignInAsync(User user)
    {
        await signIn.SignInAsync(user, isPersistent: false);
        return SignInResult.Success;
    }

    private async Task<IActionResult> FinishAsync(User user, SignInResult result, string? returnUrl, string failed)
    {
        await db.SaveChangesAsync();
        if (result.RequiresTwoFactor)
        {
            return RedirectToPage("LoginWith2fa", new { ReturnUrl = returnUrl, Input.RememberMe });
        }

        if (result.IsLockedOut)
        {
            // failed attempts lock for minutes; an administrator's lock lasts days or has no end
            ErrorMessage = user.LockoutEnd is { } end && end - DateTimeOffset.UtcNow > TimeSpan.FromDays(1)
                ? T("This account is locked. Please contact an administrator.")
                : T("Too many attempts. Try again later.");
            return Page();
        }

        if (result.IsNotAllowed)
        {
            ErrorMessage = T("Confirm your email address first; the link is in the mail we sent you.");
            return Page();
        }

        if (!result.Succeeded)
        {
            ErrorMessage = T(failed);
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

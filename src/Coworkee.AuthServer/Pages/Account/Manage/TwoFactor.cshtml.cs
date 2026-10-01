using System.Text;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Coworkee.AuthServer.Pages.Account.Manage;

[Authorize(AuthenticationSchemes = "Identity.Application")]
public sealed class TwoFactorModel(UserManager<User> users, CoworkeeDbContext db, IOptions<AuthServerOptions> options) : PageModel
{
    [BindProperty]
    public string Code { get; set; } = string.Empty;

    public bool Enabled { get; private set; }

    public string? SharedKey { get; private set; }

    public string? AuthenticatorUri { get; private set; }

    public IReadOnlyList<string>? RecoveryCodes { get; private set; }

    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        return await users.GetUserAsync(User) is { } user ? await ShowAsync(user) : Challenge();
    }

    public async Task<IActionResult> OnPostEnableAsync()
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        if (await users.GetUserAsync(User) is not { } user)
        {
            return Challenge();
        }

        var code = Code.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        if (!await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, code))
        {
            ErrorMessage = "The code is not valid.";
            return await ShowAsync(user);
        }

        await users.SetTwoFactorEnabledAsync(user, true);
        RecoveryCodes = [.. await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10) ?? []];
        await db.SaveChangesAsync(HttpContext.RequestAborted);
        Enabled = true;
        return Page();
    }

    public async Task<IActionResult> OnPostDisableAsync()
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        if (await users.GetUserAsync(User) is not { } user)
        {
            return Challenge();
        }

        await users.SetTwoFactorEnabledAsync(user, false);
        await users.ResetAuthenticatorKeyAsync(user);
        await db.SaveChangesAsync(HttpContext.RequestAborted);
        return RedirectToPage();
    }

    private async Task<IActionResult> ShowAsync(User user)
    {
        Enabled = user.TwoFactorEnabled;
        if (!Enabled)
        {
            var key = await users.GetAuthenticatorKeyAsync(user);
            if (string.IsNullOrEmpty(key))
            {
                await users.ResetAuthenticatorKeyAsync(user);
                await db.SaveChangesAsync(HttpContext.RequestAborted);
                key = await users.GetAuthenticatorKeyAsync(user);
            }

            SharedKey = Format(key!);
            var issuer = Uri.EscapeDataString(options.Value.DisplayName);
            AuthenticatorUri = $"otpauth://totp/{issuer}:{Uri.EscapeDataString(user.Email ?? user.UserName!)}?secret={key}&issuer={issuer}&digits=6";
        }

        return Page();
    }

    private static string Format(string key)
    {
        var result = new StringBuilder();
        for (var i = 0; i < key.Length; i += 4)
        {
            result.Append(key.AsSpan(i, Math.Min(4, key.Length - i))).Append(' ');
        }

        return result.ToString().TrimEnd().ToLowerInvariant();
    }
}

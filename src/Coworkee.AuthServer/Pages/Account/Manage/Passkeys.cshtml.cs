using System.Buffers.Text;
using Coworkee.Contracts.Configuration;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using static Coworkee.AuthServer.AuthTexts;

namespace Coworkee.AuthServer.Pages.Account.Manage;

[Authorize(AuthenticationSchemes = "Identity.Application")]
public sealed class PasskeysModel(
    UserManager<User> users, SignInManager<User> signIn, CoworkeeDbContext db, ISettingProvider settings,
    ISettingDefinitionManager definitions, IOptions<AuthServerOptions> options) : PageModel
{
    private const int MaxName = 100;

    public IReadOnlyList<UserPasskeyInfo> Items { get; private set; } = [];

    public string? Notice { get; private set; }

    public string? ErrorMessage { get; private set; }

    public bool CanAdd => Items.Count < Passkeys.MaxPerUser;

    public static string IdOf(UserPasskeyInfo passkey) => Base64Url.EncodeToString(passkey.CredentialId);

    public Task<IActionResult> OnGetAsync() => RunAsync(user => ShowAsync(user));

    public Task<IActionResult> OnPostCreationOptionsAsync() => RunAsync(async user =>
        (await users.GetPasskeysAsync(user)).Count >= Passkeys.MaxPerUser
            ? (IActionResult)BadRequest()
            : Content(await signIn.MakePasskeyCreationOptionsAsync(new PasskeyUserEntity
            {
                Id = user.Id.ToString(),
                Name = user.Email ?? user.UserName!,
                DisplayName = $"{user.FirstName} {user.LastName}".Trim() is { Length: > 0 } name ? name : user.Email ?? user.UserName!,
            }), "application/json"));

    public Task<IActionResult> OnPostAddAsync(string? credential, string? name) => RunAsync(async user =>
    {
        var attestation = string.IsNullOrEmpty(credential) ? null : await AttestAsync(credential);
        if (attestation is not { Succeeded: true, Passkey: { } passkey, UserEntity.Id: var owner } || owner != user.Id.ToString())
        {
            return await ShowAsync(user, error: T("The passkey could not be saved."));
        }

        if ((await users.GetPasskeysAsync(user)).Count >= Passkeys.MaxPerUser)
        {
            return await ShowAsync(user, error: T("You have the most passkeys allowed. Remove one to add another."));
        }

        passkey.Name = Clean(name) ?? T("Passkey");
        return await SaveAsync(user, await users.AddOrUpdatePasskeyAsync(user, passkey), T("The passkey was added."));
    });

    public Task<IActionResult> OnPostRenameAsync(string? id, string? name) => RunAsync(async user =>
    {
        if ((await users.GetPasskeysAsync(user)).FirstOrDefault(p => IdOf(p) == id) is not { } passkey)
        {
            return NotFound();
        }

        passkey.Name = Clean(name) ?? passkey.Name;
        return await SaveAsync(user, await users.AddOrUpdatePasskeyAsync(user, passkey), T("The passkey was renamed."));
    });

    /// <summary>The last passkey stays while it is the only way in: without a password and without an external sign-in.</summary>
    public Task<IActionResult> OnPostRemoveAsync(string? id) => RunAsync(async user =>
    {
        var all = await users.GetPasskeysAsync(user);
        if (all.FirstOrDefault(p => IdOf(p) == id) is not { } passkey)
        {
            return NotFound();
        }

        if (all.Count == 1 && !await users.HasPasswordAsync(user) && (await users.GetLoginsAsync(user)).Count == 0)
        {
            return await ShowAsync(user, error: T("This passkey is your only way to sign in. Set a password before you remove it."));
        }

        return await SaveAsync(user, await users.RemovePasskeyAsync(user, passkey.CredentialId), T("The passkey was removed."));
    });

    private async Task<IActionResult> RunAsync(Func<User, Task<IActionResult>> action)
    {
        if (!await Passkeys.EnabledAsync(settings, definitions, options.Value, HttpContext.RequestAborted))
        {
            return NotFound();
        }

        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        return await users.GetUserAsync(User) is { } user ? await action(user) : Challenge();
    }

    private async Task<PasskeyAttestationResult?> AttestAsync(string credential)
    {
        try
        {
            return await signIn.PerformPasskeyAttestationAsync(credential);
        }
        catch (InvalidOperationException)
        {
            // no attestation underway in this browser
            return null;
        }
    }

    private async Task<IActionResult> SaveAsync(User user, IdentityResult result, string done)
    {
        // the store leaves saving to the unit of work
        await db.SaveChangesAsync(HttpContext.RequestAborted);
        return await ShowAsync(user, result.Succeeded ? done : null, result.Succeeded ? null : T("The passkey could not be saved."));
    }

    private async Task<IActionResult> ShowAsync(User user, string? notice = null, string? error = null)
    {
        Notice = notice;
        ErrorMessage = error;
        Items = [.. (await users.GetPasskeysAsync(user)).OrderBy(p => p.CreatedAt)];
        return Page();
    }

    private static string? Clean(string? name) => name?.Trim() is { Length: > 0 } text ? text[..Math.Min(text.Length, MaxName)] : null;
}

using System.Text.Json;
using Coworkee.Application.Registration;
using Coworkee.AuthServer.External;
using Coworkee.AuthServer.Registration;
using Coworkee.Contracts.Configuration;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Settings;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static Coworkee.AuthServer.AuthTexts;

namespace Coworkee.AuthServer.Pages.Account;

public sealed class RegisterModel(
    AccountRegistration registration, UserManager<User> users, SignInManager<User> signIn, ExternalSignIn external, CoworkeeDbContext db, ISettingProvider settings,
    ITenantDirectory tenants, IOptions<RegistrationOptions> options, IOptions<AuthServerOptions> auth, IDataProtectionProvider protection,
    IEnumerable<IRegistrationDocumentStore> stores) : PageModel
{
    private readonly ITimeLimitedDataProtector _protector = protection.CreateProtector("Coworkee.Registration").ToTimeLimitedDataProtector();
    private Guid _tenantId;

    [BindProperty]
    public RegistrationInput Input { get; set; } = new();

    [BindProperty]
    public string Step { get; set; } = RegistrationSteps.Account;

    [BindProperty]
    public string? State { get; set; }

    public RegistrationOptions Options => options.Value;

    public IReadOnlyList<string> Steps { get; private set; } = [];

    public IReadOnlyList<Role> Roles { get; private set; } = [];

    public User? Registered { get; private set; }

    public List<string> Errors { get; } = [];

    public string ProtectedState => _protector.Protect(JsonSerializer.Serialize(Input), TimeSpan.FromHours(2));

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        Step = Steps[0];
        return Page();
    }

    /// <summary>The completion step of an external sign-up: takes the login over from the external cookie into the state.</summary>
    public async Task<IActionResult> OnGetExternalAsync(string? returnUrl)
    {
        var login = await signIn.GetExternalLoginInfoAsync();
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        if (login is null || (await external.FindOrCreateAsync(login, HttpContext.RequestAborted)).Error?.Code != ExternalSignIn.CompletionRequired
            || !await LoadAsync(external: true))
        {
            return RedirectToPage("Login", new { returnUrl });
        }

        Input = ExternalSignIn.Prefill(login, returnUrl);
        Step = Steps[0];
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? nav)
    {
        var data = Restore() ?? new RegistrationInput();
        if (!await LoadAsync(data.External is not null))
        {
            return NotFound();
        }

        var index = Math.Max(0, Steps.ToList().IndexOf(Step));
        data.Apply(Steps[index], Input);
        (Input, Step) = (data, Steps[index]);
        ModelState.Clear();
        if (nav == "back")
        {
            Step = Steps[Math.Max(0, index - 1)];
            return Page();
        }

        if (await ValidateAsync(index == Steps.Count - 1 ? Steps : [Step]) is { } failed)
        {
            Step = failed;
            return Page();
        }

        if (index < Steps.Count - 1)
        {
            Step = Steps[index + 1];
            return Page();
        }

        return await RegisterAsync();
    }

    private async Task<bool> LoadAsync(bool external = false)
    {
        var policy = await RegistrationPolicy.LoadAsync(settings, HttpContext.RequestAborted);
        if (!policy.Enabled || (!external && auth.Value.External.Mode == LoginMode.External && auth.Value.External.Providers.Count > 0)
            || await tenants.GetSystemTenantIdAsync(HttpContext.RequestAborted) is not { } tenantId)
        {
            return false;
        }

        _tenantId = tenantId;
        Roles = await registration.SelectableRolesAsync(tenantId, HttpContext.RequestAborted);
        Steps = RegistrationSteps.For(Options, Roles.Count > 0, external);
        return true;
    }

    private RegistrationInput? Restore()
    {
        try
        {
            return State is null ? null : JsonSerializer.Deserialize<RegistrationInput>(_protector.Unprotect(State));
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    /// <summary>The first of <paramref name="steps"/> with errors, or null.</summary>
    private async Task<string?> ValidateAsync(IEnumerable<string> steps)
    {
        var validator = new RegistrationValidator(Options, [.. Roles.Select(r => r.Id)]);
        foreach (var step in steps)
        {
            var result = await validator.ValidateAsync(Input, o => o.IncludeRuleSets(step), HttpContext.RequestAborted);
            Errors.AddRange(result.Errors.Select(e => e.ErrorMessage));
            if (step == RegistrationSteps.Account && result.IsValid)
            {
                foreach (var check in users.PasswordValidators)
                {
                    Errors.AddRange((await check.ValidateAsync(users, new User(), Input.Password)).Errors.Select(e => e.Description));
                }
            }

            if (step == RegistrationSteps.Documents)
            {
                Errors.AddRange(RegistrationDocuments.Validate(Options.Documents, Request.Form.Files));
            }

            if (Errors.Count > 0)
            {
                return step;
            }
        }

        return null;
    }

    private async Task<IActionResult> RegisterAsync()
    {
        var cancellationToken = HttpContext.RequestAborted;
        var login = Input.External;
        var user = new User
        {
            TenantId = _tenantId, UserName = Input.Email, Email = Input.Email, FirstName = Input.FirstName, LastName = Input.LastName, PhoneNumber = Input.PhoneNumber,
            Street = Input.Street, ZipCode = Input.ZipCode, City = Input.City, Country = Input.Country,
            EmailConfirmed = login is not null, // the provider verified the address
        };
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(user.Id, _tenantId));
        var created = await registration.CreateAsync(user, login is null ? Input.Password : null, await RegistrationPolicy.LoadAsync(settings, cancellationToken));
        if (created.Succeeded && login is not null)
        {
            created = await users.AddLoginAsync(user, new UserLoginInfo(login.Provider, login.Key, login.DisplayName));
        }

        if (!created.Succeeded)
        {
            Errors.AddRange(created.Errors.Select(e => e.Code is "DuplicateEmail" or "DuplicateUserName"
                ? T("This address cannot be used. If it is yours, try signing in or resetting the password.")
                : e.Description));
            return Page();
        }

        db.Set<IdentityUserRole<Guid>>().AddRange(Input.RoleIds.Select(id => new IdentityUserRole<Guid> { UserId = user.Id, RoleId = id }));
        if (Step == RegistrationSteps.Documents)
        {
            await RegistrationDocuments.SaveAsync(stores.Last(), user, Options.Documents, Request.Form.Files, cancellationToken);
        }

        await registration.AnnounceAsync(user, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (login is not null && user.IsActive)
        {
            await signIn.SignInAsync(user, isPersistent: false, login.Provider);
            user.LastLoginAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return LocalRedirect(Url.IsLocalUrl(Input.ReturnUrl) ? Input.ReturnUrl : "/");
        }

        Registered = user;
        return Page();
    }
}

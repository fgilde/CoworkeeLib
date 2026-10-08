using System.Text;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore.Http;
using Coworkee.AspNetCore;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Modularity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Mailing;
using Coworkee.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Coworkee.Account;

public static class AccountSettings
{
    public const string AllowRegistration = "Account.AllowRegistration";
}

public interface IAccountMailer
{
    Task SendPasswordResetAsync(User user, CancellationToken cancellationToken);

    Task SendEmailConfirmationAsync(User user, CancellationToken cancellationToken);

    Task SendRegistrationPendingAsync(User user, CancellationToken cancellationToken);
}

public static class AccountTokens
{
    public static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    public static string? Decode(string? code)
    {
        try
        {
            return code is null ? null : Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

internal sealed class AccountMailer(UserManager<User> users, IMailSender mails, IOptions<AccountOptions> options) : IAccountMailer
{
    public async Task SendPasswordResetAsync(User user, CancellationToken cancellationToken)
    {
        var token = await users.GeneratePasswordResetTokenAsync(user);
        await mails.QueueAsync(user.Email!, "Identity.ResetPassword", new { user = Model(user), reset_url = Link("ResetPassword", user, token) }, null, cancellationToken);
    }

    public async Task SendEmailConfirmationAsync(User user, CancellationToken cancellationToken)
    {
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        await mails.QueueAsync(user.Email!, "Identity.ConfirmEmail", new { user = Model(user), confirm_url = Link("ConfirmEmail", user, token) }, null, cancellationToken);
    }

    public Task SendRegistrationPendingAsync(User user, CancellationToken cancellationToken) =>
        mails.QueueAsync(user.Email!, "Identity.RegistrationPending", new { user = Model(user) }, null, cancellationToken);

    private string Link(string page, User user, string token) =>
        $"{options.Value.PublicAuthUrl.TrimEnd('/')}/Account/{page}?userId={user.Id}&code={AccountTokens.Encode(token)}";

    private static object Model(User user) => new { first_name = user.FirstName ?? user.Email, last_name = user.LastName, email = user.Email };
}

[RequiresPermission(IdentityPermissions.Users.Manage)]
public sealed record SendPasswordReset(Guid UserId) : ICommand<Result>;

internal sealed class SendPasswordResetHandler(CoworkeeDbContext db, ICurrentUser currentUser, IAccountMailer mailer) : IHandler<SendPasswordReset, Result>
{
    public async Task<Result> HandleAsync(SendPasswordReset command, CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == command.UserId && u.TenantId == currentUser.TenantId, cancellationToken);
        if (user is null)
        {
            return Error.NotFound("identity.user_not_found", "The user does not exist.");
        }

        if (!user.IsActive || string.IsNullOrEmpty(user.Email))
        {
            return Error.Conflict("account.user_inactive", "Inactive users or users without an email address cannot reset their password.");
        }

        await mailer.SendPasswordResetAsync(user, cancellationToken);
        return Result.Success();
    }
}

internal sealed class AccountSettingDefinitions : ISettingDefinitionContributor
{
    public void Define(SettingDefinitionContext context) =>
        context.Group("Account", "Accounts")
            .Add(AccountSettings.AllowRegistration, "Allow self registration", SettingType.Bool, [SettingScope.Global], "false",
                description: "New accounts stay inactive until an administrator activates them.");
}

[DependsOn(typeof(CoworkeeIdentityModule), typeof(CoworkeeMailingModule))]
public sealed class CoworkeeAccountModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.Configure<AccountOptions>(context.Configuration.GetSection(AccountOptions.Section));
        context.Services.AddMessagingFromAssembly(typeof(CoworkeeAccountModule).Assembly);
        context.Services.AddScoped<IAccountMailer, AccountMailer>();
        context.Services.AddSingleton<ISettingDefinitionContributor, AccountSettingDefinitions>();
    }

    public void ConfigureApplication(WebApplication app) =>
        app.MapCoworkeeApi("/api/v1/identity").WithTags("Identity").RequireAuthorization()
            .MapPost("/users/{id:guid}/password-reset", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new SendPasswordReset(id), ct).ToHttpResult());
}

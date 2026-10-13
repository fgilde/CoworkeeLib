using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Mailing;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Results;
using Coworkee.Core.Security;

namespace Coworkee.Mailing;

[AiTool(Exclude = true)]
[RequiresPermission(SettingsPermissions.Manage)]
public sealed record TestMailConnection(MailConnectionTestRequest Request) : ICommand<Result<MailConnectionTestDto>>;

internal sealed class TestMailConnectionHandler(
    MailConnectionResolver resolver, IEnumerable<IMailTransport> transports, ICurrentUser currentUser, IUserDirectory users)
    : IHandler<TestMailConnection, Result<MailConnectionTestDto>>
{
    private static readonly string[] SecretNames = [MailSettings.Password, MailSettings.ApiKey];

    public async Task<Result<MailConnectionTestDto>> HandleAsync(TestMailConnection command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || await users.GetEmailAsync(userId, cancellationToken) is not { Length: > 0 } email)
        {
            return Error.Validation("User", "The current user has no email address.");
        }

        var request = command.Request;
        MailConnection? connection = null;
        IMailTransport transport;
        try
        {
            connection = await resolver.ResolveDraftAsync(request, cancellationToken);
            transport = transports.FirstOrDefault(t => t.Provider == connection.Provider)
                ?? throw new InvalidOperationException($"No mail transport is registered for provider '{connection.Provider}'.");
        }
        catch (InvalidOperationException exception)
        {
            return new MailConnectionTestDto(false, Redact(exception.Message, request, connection));
        }

        try
        {
            await transport.SendAsync(
                new MailEnvelope(connection[MailSettings.From] ?? "noreply@localhost", email, "Coworkee test mail", "<p>This is a test mail from Coworkee. Your mail settings work.</p>"),
                connection,
                cancellationToken);
            return new MailConnectionTestDto(true, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // an organisation chooses its SMTP host: its admins must not read network errors of the server (a probe of the internal network)
            var error = await resolver.IsSystemAsync(cancellationToken)
                ? Redact(exception.Message, request, connection)
                : $"The test mail could not be sent. ({exception.GetType().Name})";
            return new MailConnectionTestDto(false, error);
        }
    }

    // transports hide the key they send with; this covers every secret of the form and of the stored settings
    private static string Redact(string message, MailConnectionTestRequest request, MailConnection? connection)
    {
        foreach (var name in SecretNames)
        {
            foreach (var secret in new[] { request.Values.GetValueOrDefault(name), connection?[name] })
            {
                if (secret is { Length: > 0 })
                {
                    message = message.Replace(secret, "***", StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        return message;
    }
}

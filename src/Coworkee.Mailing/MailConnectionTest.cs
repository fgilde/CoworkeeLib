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

        var draft = command.Request.Values;
        try
        {
            var connection = await resolver.ResolveDraftAsync(draft, cancellationToken);
            var transport = transports.FirstOrDefault(t => t.Provider == connection.Provider)
                ?? throw new InvalidOperationException($"No mail transport is registered for provider '{connection.Provider}'.");
            await transport.SendAsync(
                new MailEnvelope(connection[MailSettings.From] ?? "noreply@localhost", email, "Coworkee test mail", "<p>This is a test mail from Coworkee. Your mail settings work.</p>"),
                connection,
                cancellationToken);
            return new MailConnectionTestDto(true, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new MailConnectionTestDto(false, Redact(exception.Message, draft));
        }
    }

    // the transports already hide the key they used; this also covers a typed one that never got that far
    private static string Redact(string message, IReadOnlyDictionary<string, string?> draft)
    {
        foreach (var name in SecretNames)
        {
            if (draft.GetValueOrDefault(name) is { Length: > 0 } secret)
            {
                message = message.Replace(secret, "***", StringComparison.OrdinalIgnoreCase);
            }
        }

        return message;
    }
}

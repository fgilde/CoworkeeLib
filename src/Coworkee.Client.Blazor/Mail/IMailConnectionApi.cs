using Coworkee.Contracts.Mailing;

namespace Coworkee.Client.Blazor.Mail;

/// <summary>The test mail of the mail settings page; the connection itself is saved as mail settings.</summary>
public interface IMailConnectionApi
{
    Task<MailConnectionTestDto> TestAsync(MailConnectionTestRequest request, CancellationToken cancellationToken = default);
}

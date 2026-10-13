using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Mailing;

namespace Coworkee.Client.Blazor.Mail;

internal sealed class MailConnectionApi(HttpClient http) : ApiClientBase(http), IMailConnectionApi
{
    public Task<MailConnectionTestDto> TestAsync(MailConnectionTestRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<MailConnectionTestDto>(HttpMethod.Post, "api/v1/mail/connection/test", request, cancellationToken);
}

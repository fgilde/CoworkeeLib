using System.Net.Http.Headers;
using System.Net.Http.Json;
using Coworkee.Contracts.Mailing;

namespace Coworkee.Mailing;

internal sealed class SendGridMailTransport(IHttpClientFactory httpClients) : IMailTransport
{
    public string Provider => MailProviders.SendGrid;

    public async Task SendAsync(MailEnvelope mail, MailConnection connection, CancellationToken cancellationToken)
    {
        var apiKey = connection[MailSettings.ApiKey];
        var (address, name) = MailHttp.Sender(mail.From);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.sendgrid.com/v3/mail/send")
        {
            Content = JsonContent.Create(new
            {
                personalizations = new[] { new { to = new[] { new { email = mail.To } } } },
                from = new { email = address, name },
                subject = mail.Subject,
                content = new[] { new { type = "text/html", value = mail.HtmlBody } },
            }, options: MailHttp.Json),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await httpClients.CreateClient().SendAsync(request, cancellationToken);
        await MailHttp.EnsureSuccessAsync(response, "SendGrid", apiKey, cancellationToken);
    }
}

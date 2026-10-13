using System.Net.Http.Headers;
using System.Text;
using Coworkee.Contracts.Mailing;

namespace Coworkee.Mailing;

internal sealed class MailgunMailTransport(IHttpClientFactory httpClients) : IMailTransport
{
    public string Provider => MailProviders.Mailgun;

    public async Task SendAsync(MailEnvelope mail, MailConnection connection, CancellationToken cancellationToken)
    {
        var apiKey = connection[MailSettings.ApiKey];
        var host = connection[MailSettings.MailgunRegion] == "eu" ? "api.eu.mailgun.net" : "api.mailgun.net";
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{host}/v3/{Uri.EscapeDataString(connection[MailSettings.MailgunDomain] ?? string.Empty)}/messages")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["from"] = mail.From,
                ["to"] = mail.To,
                ["subject"] = mail.Subject,
                ["html"] = mail.HtmlBody,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"api:{apiKey}")));

        using var response = await httpClients.CreateClient().SendAsync(request, cancellationToken);
        await MailHttp.EnsureSuccessAsync(response, "Mailgun", apiKey, cancellationToken);
    }
}

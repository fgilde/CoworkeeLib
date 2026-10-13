using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Mailing;

namespace Coworkee.Mailing;

internal sealed class MailchimpMailTransport(IHttpClientFactory httpClients) : IMailTransport
{
    public string Provider => MailProviders.Mailchimp;

    public async Task SendAsync(MailEnvelope mail, MailConnection connection, CancellationToken cancellationToken)
    {
        var apiKey = connection[MailSettings.ApiKey];
        var (address, name) = MailHttp.Sender(mail.From);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://mandrillapp.com/api/1.0/messages/send")
        {
            Content = JsonContent.Create(new
            {
                key = apiKey,
                message = new
                {
                    html = mail.HtmlBody,
                    subject = mail.Subject,
                    from_email = address,
                    from_name = name,
                    to = new[] { new { email = mail.To, type = "to" } },
                },
            }, options: MailHttp.Json),
        };

        using var response = await httpClients.CreateClient().SendAsync(request, cancellationToken);
        await MailHttp.EnsureSuccessAsync(response, "Mailchimp", apiKey, cancellationToken);

        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        foreach (var entry in result.RootElement.EnumerateArray())
        {
            if (entry.GetProperty("status").GetString() is "rejected" or "invalid")
            {
                var reason = entry.TryGetProperty("reject_reason", out var value) ? value.GetString() : null;
                throw new InvalidOperationException($"Mailchimp {entry.GetProperty("status").GetString()} the mail{(reason is null ? "." : $": {reason}")}");
            }
        }
    }
}

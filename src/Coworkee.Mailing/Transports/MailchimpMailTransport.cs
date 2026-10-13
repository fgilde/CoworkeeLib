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

        foreach (var entry in Entries(await response.Content.ReadAsStringAsync(cancellationToken)))
        {
            var status = entry.GetProperty("status").GetString();
            if (status is "rejected" or "invalid")
            {
                var reason = entry.TryGetProperty("reject_reason", out var value) ? value.GetString() : null;
                throw new InvalidOperationException($"Mailchimp {status} the mail{(reason is null ? "." : $": {reason}")}");
            }
        }
    }

    private static List<JsonElement> Entries(string json)
    {
        try
        {
            using var result = JsonDocument.Parse(json);
            var entries = result.RootElement.ValueKind == JsonValueKind.Array
                ? result.RootElement.EnumerateArray().Select(e => e.Clone()).ToList()
                : [];
            if (entries.Count > 0 && entries.All(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String))
            {
                return entries;
            }
        }
        catch (JsonException)
        {
        }

        throw new InvalidOperationException("Mailchimp returned an unexpected response.");
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using MimeKit;

namespace Coworkee.Mailing;

internal static class MailHttp
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static (string Address, string? Name) Sender(string from)
    {
        var mailbox = MailboxAddress.Parse(from);
        return (mailbox.Address, string.IsNullOrEmpty(mailbox.Name) ? null : mailbox.Name);
    }

    public static async Task EnsureSuccessAsync(HttpResponseMessage response, string provider, string? secret, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!string.IsNullOrEmpty(secret))
        {
            body = body.Replace(secret, "***", StringComparison.Ordinal);
        }

        throw new InvalidOperationException($"{provider} rejected the mail with {(int)response.StatusCode} {response.ReasonPhrase}: {(body.Length > 500 ? body[..500] : body)}");
    }
}

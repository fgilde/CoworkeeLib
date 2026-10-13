using System.Net;
using System.Text;
using System.Text.Encodings.Web;
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
        foreach (var form in Forms(secret))
        {
            body = body.Replace(form, "***", StringComparison.OrdinalIgnoreCase);
        }

        throw new InvalidOperationException($"{provider} rejected the mail with {(int)response.StatusCode} {response.ReasonPhrase}: {(body.Length > 500 ? body[..500] : body)}");
    }

    private static IEnumerable<string> Forms(string? secret) => string.IsNullOrEmpty(secret)
        ? []
        : new[]
        {
            secret,
            JsonEncodedText.Encode(secret).ToString(),
            JsonEncodedText.Encode(secret, JavaScriptEncoder.UnsafeRelaxedJsonEscaping).ToString(),
            Uri.EscapeDataString(secret),
            WebUtility.UrlEncode(secret),
            Convert.ToBase64String(Encoding.UTF8.GetBytes(secret)),
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"api:{secret}")),
        }.Distinct();
}

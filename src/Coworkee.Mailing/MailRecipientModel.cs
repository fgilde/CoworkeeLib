namespace Coworkee.Mailing;

public sealed record MailRecipient(Guid Id, string Email, string? FirstName, string? LastName, string? Culture);

public static class MailRecipientModel
{
    public static object Sample { get; } = For(
        new MailRecipient(Guid.Empty, "ada@example.com", "Ada", "Lovelace", "en"),
        "Coworkee",
        "https://app.example.com",
        "Example Inc.",
        "https://app.example.com/unsubscribe",
        "Spring news");

    public static object For(MailRecipient recipient, string? appName, string? appUrl, string? organisationName, string? unsubscribeUrl, string? campaignName)
    {
        var displayName = $"{recipient.FirstName} {recipient.LastName}".Trim();
        return new
        {
            user = new
            {
                first_name = recipient.FirstName,
                last_name = recipient.LastName,
                display_name = displayName.Length > 0 ? displayName : recipient.Email,
                email = recipient.Email,
                culture = recipient.Culture,
            },
            app = new { name = appName, url = appUrl },
            organisation = new { name = organisationName },
            unsubscribe_url = unsubscribeUrl,
            campaign = campaignName is null ? null : (object)new { name = campaignName },
        };
    }
}

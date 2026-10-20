using Coworkee.Mailing;

namespace Coworkee.Reporting.Subscriptions;

/// <summary>The subscription mail; its texts ship with the other mail templates in every language.</summary>
internal sealed class ReportMailTemplates : IMailTemplateContributor
{
    public void Define(MailTemplateContext context) =>
        context.Add(new(ReportSubscriptionMail.Template, "Report subscription", new
        {
            user = new { first_name = "Ada", email = "ada@example.com" },
            report = new { name = "Sales", link = "https://example.com/reports/1", date = "2026-10-20" },
            widgets = new object[]
            {
                new { title = "Revenue", kpi = true, value = "12,500 €", color = "#2e7d32", symbol = "✓", band = "On target", headers = Array.Empty<string>(), rows = Array.Empty<string[]>(), more = false, error = string.Empty },
                new { title = "By region", kpi = false, value = string.Empty, color = "#16140f", symbol = string.Empty, band = string.Empty, headers = new[] { "Region", "Count" },
                    rows = new[] { new[] { "North", "12" }, new[] { "South", "8" } }, more = true, latest = false, error = string.Empty },
            },
            attachment = true,
            unsubscribe_link = "https://example.com/api/v1/reports/subscriptions/unsubscribe?token=x",
        }, EmbeddedTemplates.Load(typeof(IMailSender).Assembly, ReportSubscriptionMail.Template)));
}

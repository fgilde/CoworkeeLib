using System.Globalization;
using System.Text.Json;
using Coworkee.Application.Authorization;
using Coworkee.Application.Localization;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Mailing;
using Coworkee.Reporting.Querying;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Coworkee.Reporting.Subscriptions;

/// <summary>
/// Writes and queues the mail of one subscription. Runs in a scope of the subscriber: the report must still be shared with them, and every widget shows what
/// their permissions and row filters let them see. KPIs and the first rows of the other widgets go into the body, all rows into an Excel workbook.
/// </summary>
internal sealed partial class ReportSubscriptionMail(
    IServiceProvider services, CoworkeeDbContext db, ReportAccess access, ReportQueries queries, IPermissionChecker permissions, IMailSender mails, IUserDirectory users,
    ReportMailLinks links, IEnumerable<ITextTranslator> translators, IEnumerable<IDefaultLanguage> defaultLanguages, ICurrentUser currentUser, ILogger<ReportSubscriptionMail> logger)
{
    public const string Template = "Reports.Subscription";
    public const int MailRows = 10;
    public const int SheetRows = 1000;

    /// <summary>Sends the mail; <paramref name="advance"/> moves the next run on (the schedule did it), a manual send leaves it.</summary>
    public async Task RunAsync(Guid subscriptionId, DateTimeOffset now, bool advance, CancellationToken cancellationToken)
    {
        var subscription = await db.Set<ReportSubscription>().FirstOrDefaultAsync(s => s.Id == subscriptionId && s.UserId == currentUser.UserId, cancellationToken);
        if (subscription is null)
        {
            return;
        }

        string? error;
        try
        {
            error = await SendAsync(subscription, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // the half written mail is dropped, the subscription keeps its error and tries again at the next run
            LogFailed(exception, subscriptionId);
            foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList())
            {
                entry.State = EntityState.Detached;
            }

            error = exception.Message;
        }

        subscription.LastError = error is null ? null : error[..Math.Min(error.Length, 2000)];
        subscription.LastSentAt = error is null ? now : subscription.LastSentAt;
        if (advance)
        {
            subscription.NextRunAt = ReportSchedules.Next(subscription.Schedule, now).ToUniversalTime();
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <returns>Why nothing was sent, null when the mail is queued.</returns>
    private async Task<string?> SendAsync(ReportSubscription subscription, CancellationToken cancellationToken)
    {
        if (!await permissions.IsGrantedAsync(ReportPermissions.View, cancellationToken))
        {
            return "The subscriber may no longer view reports.";
        }

        if (await (await access.VisibleAsync(cancellationToken)).AsNoTracking().FirstOrDefaultAsync(r => r.Id == subscription.ReportId, cancellationToken) is not { } report)
        {
            return "The report is no longer shared with the subscriber.";
        }

        if (!(await users.GetActiveEmailsAsync([subscription.UserId], cancellationToken)).TryGetValue(subscription.UserId, out var email))
        {
            return "The subscriber has no active e-mail address.";
        }

        var culture = await RecipientCultures.OfAsync(db, subscription.UserId, subscription.TenantId, defaultLanguages, cancellationToken);
        var definition = ReportHandlers.Read(report);
        if (subscription.Filter is { Length: > 0 } json && JsonSerializer.Deserialize<ReportSubscriptionFilter>(json, ReportHandlers.Json) is { } own)
        {
            definition.DateRange = own.DateRange ?? definition.DateRange;
            definition.Filters = own.Filters is null ? definition.Filters : [.. own.Filters];
        }

        var zone = ReportSchedules.Zone(subscription.TimeZone) ?? TimeZoneInfo.Utc;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
        var widgets = new List<object>();
        var sheets = new List<ReportSheetRequest>();
        var sources = await queries.SourcesAsync(cancellationToken);
        foreach (var widget in definition.Widgets.Where(w => w.Query is { EntitySet.Length: > 0 } && w.Type is not (WidgetType.Markdown or WidgetType.Image or WidgetType.Embed)).OrderBy(w => w.Y).ThenBy(w => w.X))
        {
            var section = await new ReportMailWidget(services, queries, sources, definition, widget, today, culture, Text).BuildAsync(cancellationToken);
            widgets.Add(section.Model);
            if (section.Sheet is { } sheet)
            {
                sheets.Add(sheet);
            }
        }

        var name = Text(report.Name, culture);
        List<MailAttachment> attachments = subscription.Attachment && sheets.Count > 0
            ? [new MailAttachment(FileName(name) + ".xlsx", ReportSheet.ContentType, ReportSheet.Write(sheets))]
            : [];
        await mails.QueueWithAttachmentsAsync(email, Template, new
        {
            user = new { first_name = (await users.GetDisplayNamesAsync([subscription.UserId], cancellationToken)).GetValueOrDefault(subscription.UserId) ?? email, email },
            report = new { name, link = links.Report(report.Id), date = today.ToString("d", culture) },
            widgets,
            attachment = attachments.Count > 0,
            unsubscribe_link = links.Unsubscribe(subscription.Id, subscription.TenantId),
        }, culture.Name, attachments, cancellationToken);
        return null;
    }

    private string Text(string text, CultureInfo culture) => translators.Translate(text, culture);

    private static string FileName(string name)
    {
        var clean = string.Concat(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
        return clean.Length == 0 ? "Report" : clean[..Math.Min(80, clean.Length)];
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The mail of report subscription {SubscriptionId} could not be written")]
    private partial void LogFailed(Exception exception, Guid subscriptionId);
}

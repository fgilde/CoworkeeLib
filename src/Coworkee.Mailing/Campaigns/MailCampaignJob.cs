using Coworkee.BackgroundJobs;
using Coworkee.Contracts.Mailing;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Coworkee.Mailing;

/// <summary>Queues one mail per eligible recipient of a scheduled campaign, page by page, as the user who scheduled it; delivery stays with <see cref="SendMailJob"/>.</summary>
internal sealed class MailCampaignJob(
    CoworkeeDbContext db, IMailRecipientDirectory directory, IMailSender sender, CampaignMailModels models, ICurrentUser currentUser, TimeProvider clock, ILogger<MailCampaignJob> logger)
    : IBackgroundJob<Guid>
{
    private const int MaxError = 2000;

    internal int PageSize { get; set; } = 500;

    public async Task ExecuteAsync(Guid campaignId, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId;
        var campaigns = db.Set<MailCampaign>().Where(c => c.Id == campaignId && c.TenantId == tenantId);
        // shortcut: a worker shutdown mid-run leaves the campaign in Sending (no resume), add a re-claim once that shows up
        var now = (DateTimeOffset?)clock.GetUtcNow();
        var claimed = await campaigns.Where(c => c.Status == MailCampaignStatus.Scheduled).ExecuteUpdateAsync(update => update
            .SetProperty(c => c.Status, MailCampaignStatus.Sending)
            .SetProperty(c => c.StartedAt, now), cancellationToken);
        if (claimed == 0)
        {
            return;
        }

        var error = await QueueAsync(await campaigns.AsNoTracking().SingleAsync(cancellationToken), campaigns, cancellationToken);
        var finished = await campaigns.Where(c => c.Status == MailCampaignStatus.Sending).ExecuteUpdateAsync(update => update
            .SetProperty(c => c.Status, error is null ? MailCampaignStatus.Sent : MailCampaignStatus.Failed)
            .SetProperty(c => c.FinishedAt, (DateTimeOffset?)clock.GetUtcNow())
            .SetProperty(c => c.Error, error), cancellationToken);
        if (finished == 0)
        {
            // cancelled while the last page was being queued
            await db.Set<OutgoingMail>().Where(m => m.CampaignId == campaignId && m.Status == OutgoingMailStatus.Queued).ExecuteDeleteAsync(cancellationToken);
        }
    }

    /// <returns>The error that ended the run, null when it ended normally or because the campaign was cancelled.</returns>
    private async Task<string?> QueueAsync(MailCampaign campaign, IQueryable<MailCampaign> campaigns, CancellationToken cancellationToken)
    {
        try
        {
            var source = RecipientSourceJson.Read(campaign.RecipientSource);
            var model = await models.ForAsync(campaign, cancellationToken);
            Guid? after = null;
            var total = 0;
            do
            {
                if (await campaigns.AsNoTracking().Select(c => c.Status).SingleAsync(cancellationToken) != MailCampaignStatus.Sending)
                {
                    return null;
                }

                var page = await directory.PageAsync(source, after, PageSize, cancellationToken);
                if (!page.IsSuccess)
                {
                    return Shorten(page.Error!.Message);
                }

                string? failure = null;
                var queued = 0;
                foreach (var recipient in page.Value.Recipients)
                {
                    try
                    {
                        await sender.QueueAsync(recipient.Email, campaign.TemplateName, model(recipient), recipient.Culture, campaign.Id, cancellationToken);
                        queued++;
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        logger.LogWarning(exception, "The campaign mail for {UserId} could not be queued.", recipient.Id);
                        failure = exception.Message;
                    }
                }

                if (failure is not null && queued == 0)
                {
                    return Shorten(failure);
                }

                await db.SaveChangesAsync(cancellationToken);
                total += queued;
                await campaigns.ExecuteUpdateAsync(update => update.SetProperty(c => c.Total, total), cancellationToken);
                after = page.Value.Next;
            }
            while (after is not null);

            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Campaign {CampaignId} failed.", campaign.Id);
            return Shorten(exception.Message);
        }
    }

    private static string Shorten(string message) => message.Length > MaxError ? message[..MaxError] : message;
}

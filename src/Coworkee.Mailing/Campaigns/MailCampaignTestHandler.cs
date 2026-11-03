using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;

namespace Coworkee.Mailing;

internal sealed class MailCampaignTestHandler(
    CoworkeeDbContext db, ICurrentUser currentUser, IMailTemplateCatalog catalog, IMailSender sender, IUserDirectory users, CampaignMailModels models)
    : IHandler<SendMailCampaignTest, Result>
{
    public async Task<Result> HandleAsync(SendMailCampaignTest command, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId;
        if (await db.FindAsync(tenantId, command.Id, cancellationToken) is not { } campaign)
        {
            return MailCampaigns.NotFound;
        }

        if (await catalog.CheckTemplateAsync(campaign.TemplateName, cancellationToken) is { } template)
        {
            return template;
        }

        if (currentUser.UserId is not { } userId || await users.GetEmailAsync(userId, cancellationToken) is not { Length: > 0 } email)
        {
            return Error.Validation("User", "The current user has no email address.");
        }

        // shortcut: the display name stands for the first name, the user directory has no separate parts
        var name = (await users.GetDisplayNamesAsync([userId], cancellationToken)).GetValueOrDefault(userId);
        var culture = (await RecipientCultures.ForAsync(db, new Dictionary<Guid, Guid?> { [userId] = tenantId }, cancellationToken))[userId]?.Name;
        var recipient = new MailRecipient(userId, email, name, null, culture);
        await sender.QueueAsync(email, campaign.TemplateName, (await models.ForAsync(campaign, cancellationToken, requireUnsubscribeLink: false))(recipient), culture, cancellationToken);
        return Result.Success();
    }
}

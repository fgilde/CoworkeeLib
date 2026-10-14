using Coworkee.Contracts.Configuration;
using Coworkee.Core.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Coworkee.Mailing;

/// <summary>The template model of a campaign mail for each recipient; the app and organisation parts are looked up once per campaign.</summary>
internal sealed class CampaignMailModels(
    IOptions<NotificationOptions> notifications, IOptions<KeycloakThemeOptions> theme, ICurrentUser currentUser, IServiceProvider services, IMailUnsubscribeLinks links)
{
    /// <summary>Campaign mails link back to the app (unsubscribe), so they need its absolute address.</summary>
    public bool HasPublicUrl => !string.IsNullOrWhiteSpace(notifications.Value.PublicAppUrl);

    /// <param name="requireUnsubscribeLink">When false and no absolute address can be built, the model has no unsubscribe link and the layout shows no footer.</param>
    public async Task<Func<MailRecipient, object>> ForAsync(MailCampaign campaign, CancellationToken cancellationToken, bool requireUnsubscribeLink = true)
    {
        var tenantId = currentUser.TenantId;
        var organisation = tenantId is { } id && services.GetService<ITenantDirectory>() is { } tenants ? await tenants.GetNameAsync(id, cancellationToken) : null;
        return recipient => MailRecipientModel.For(recipient, theme.Value.AppName, notifications.Value.PublicAppUrl, organisation, UnsubscribeUrl(recipient.Id, tenantId, requireUnsubscribeLink), campaign.Name);
    }

    private string? UnsubscribeUrl(Guid userId, Guid? tenantId, bool required)
    {
        try
        {
            return links.Create(userId, tenantId);
        }
        catch (InvalidOperationException) when (!required)
        {
            return null;
        }
    }
}

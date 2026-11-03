using Coworkee.Contracts.Mailing;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Mailing;

internal static class MailCampaigns
{
    public static readonly Error NotFound = Error.NotFound("mail.campaign_not_found", "The campaign does not exist.");

    public static readonly Error PublicUrlRequired = Error.Validation("PublicAppUrl", "Set Coworkee:Notifications:PublicAppUrl so mails can link back to the app.");

    public static string Describe(Error error) =>
        error.Details is { Count: > 0 } details ? string.Join(" ", details.Values.SelectMany(messages => messages)) : error.Message;

    public static Error WrongState(string action, MailCampaignStatus status) =>
        Error.Conflict("mail.campaign_state", $"A campaign that is {status.ToString().ToLowerInvariant()} cannot be {action}.");

    public static Task<MailCampaign?> FindAsync(this CoworkeeDbContext db, Guid? tenantId, Guid id, CancellationToken cancellationToken) =>
        db.Set<MailCampaign>().FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, cancellationToken);

    public static async Task<Error?> CheckTemplateAsync(this IMailTemplateCatalog catalog, string? name, CancellationToken cancellationToken) =>
        !string.IsNullOrEmpty(name) && await catalog.FindAsync(name, cancellationToken) is { } definition && catalog.IsCustom(definition)
            ? null
            : Error.Validation(nameof(SaveMailCampaignRequest.TemplateName), "Choose one of your own mail templates.");
}

using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Mailing;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Mailing;

[RequiresPermission(MailPermissions.Campaigns.Send)]
public sealed record GetMailCampaigns : IQuery<Result<IReadOnlyList<MailCampaignDto>>>;

[RequiresPermission(MailPermissions.Campaigns.Send)]
public sealed record GetMailCampaign(Guid Id) : IQuery<Result<MailCampaignDto>>;

[RequiresPermission(MailPermissions.Campaigns.Send)]
public sealed record CreateMailCampaign(SaveMailCampaignRequest Request) : ICommand<Result<MailCampaignDto>>;

[RequiresPermission(MailPermissions.Campaigns.Send)]
public sealed record UpdateMailCampaign(Guid Id, SaveMailCampaignRequest Request) : ICommand<Result<MailCampaignDto>>;

[RequiresPermission(MailPermissions.Campaigns.Send)]
public sealed record DeleteMailCampaign(Guid Id) : ICommand<Result>;

[RequiresPermission(MailPermissions.Campaigns.Send)]
public sealed record ScheduleMailCampaign(Guid Id, ScheduleMailCampaignRequest Request) : ICommand<Result<MailCampaignDto>>;

[RequiresPermission(MailPermissions.Campaigns.Send)]
public sealed record CancelMailCampaign(Guid Id) : ICommand<Result>;

[RequiresPermission(MailPermissions.Campaigns.Send)]
public sealed record SendMailCampaignTest(Guid Id) : ICommand<Result>;

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

internal sealed class MailCampaignHandlers(CoworkeeDbContext db, ICurrentUser currentUser, IMailTemplateCatalog catalog, IMailRecipientDirectory directory, CampaignMailModels models, TimeProvider clock)
    : IHandler<GetMailCampaigns, Result<IReadOnlyList<MailCampaignDto>>>,
      IHandler<GetMailCampaign, Result<MailCampaignDto>>,
      IHandler<CreateMailCampaign, Result<MailCampaignDto>>,
      IHandler<UpdateMailCampaign, Result<MailCampaignDto>>,
      IHandler<DeleteMailCampaign, Result>,
      IHandler<ScheduleMailCampaign, Result<MailCampaignDto>>,
      IHandler<CancelMailCampaign, Result>
{
    // shortcut: the list is not paged, add paging when tenants send hundreds of campaigns
    public async Task<Result<IReadOnlyList<MailCampaignDto>>> HandleAsync(GetMailCampaigns query, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId;
        var campaigns = await db.Set<MailCampaign>().AsNoTracking().Where(c => c.TenantId == tenantId).OrderByDescending(c => c.CreatedAt).ToListAsync(cancellationToken);
        var counts = await CountsAsync(campaigns.Select(c => c.Id).ToList(), cancellationToken);
        return Result<IReadOnlyList<MailCampaignDto>>.Success(campaigns.Select(c => ToDto(c, counts)).ToList());
    }

    public async Task<Result<MailCampaignDto>> HandleAsync(GetMailCampaign query, CancellationToken cancellationToken) =>
        await db.FindAsync(currentUser.TenantId, query.Id, cancellationToken) is { } campaign ? await DtoAsync(campaign, cancellationToken) : MailCampaigns.NotFound;

    public async Task<Result<MailCampaignDto>> HandleAsync(CreateMailCampaign command, CancellationToken cancellationToken)
    {
        if (await CheckAsync(command.Request, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var campaign = new MailCampaign
        {
            TenantId = currentUser.TenantId,
            Name = command.Request.Name.Trim(),
            TemplateName = command.Request.TemplateName,
            RecipientSource = RecipientSourceJson.Write(command.Request.Recipients),
            Status = MailCampaignStatus.Draft,
            CreatedBy = currentUser.UserId ?? Guid.Empty,
            CreatedAt = clock.GetUtcNow(),
        };
        db.Add(campaign);
        return ToDto(campaign, new Dictionary<Guid, Counts>());
    }

    public async Task<Result<MailCampaignDto>> HandleAsync(UpdateMailCampaign command, CancellationToken cancellationToken)
    {
        if (await db.FindAsync(currentUser.TenantId, command.Id, cancellationToken) is not { } campaign)
        {
            return MailCampaigns.NotFound;
        }

        if (campaign.Status != MailCampaignStatus.Draft)
        {
            return MailCampaigns.WrongState("changed", campaign.Status);
        }

        if (await CheckAsync(command.Request, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        campaign.Name = command.Request.Name.Trim();
        campaign.TemplateName = command.Request.TemplateName;
        campaign.RecipientSource = RecipientSourceJson.Write(command.Request.Recipients);
        return await DtoAsync(campaign, cancellationToken);
    }

    public async Task<Result> HandleAsync(DeleteMailCampaign command, CancellationToken cancellationToken)
    {
        if (await db.FindAsync(currentUser.TenantId, command.Id, cancellationToken) is not { } campaign)
        {
            return MailCampaigns.NotFound;
        }

        if (campaign.Status is MailCampaignStatus.Scheduled or MailCampaignStatus.Sending)
        {
            return MailCampaigns.WrongState("deleted", campaign.Status);
        }

        db.Remove(campaign);
        return Result.Success();
    }

    public async Task<Result<MailCampaignDto>> HandleAsync(ScheduleMailCampaign command, CancellationToken cancellationToken)
    {
        if (await db.FindAsync(currentUser.TenantId, command.Id, cancellationToken) is not { } campaign)
        {
            return MailCampaigns.NotFound;
        }

        if (campaign.Status != MailCampaignStatus.Draft)
        {
            return MailCampaigns.WrongState("scheduled", campaign.Status);
        }

        if (!models.HasPublicUrl)
        {
            return MailCampaigns.PublicUrlRequired;
        }

        if (await CheckAsync(new SaveMailCampaignRequest(campaign.Name, campaign.TemplateName, RecipientSourceJson.Read(campaign.RecipientSource)), cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var now = clock.GetUtcNow();
        if (command.Request.At is { } past && past < now.AddMinutes(-1))
        {
            return Error.Validation(nameof(ScheduleMailCampaignRequest.At), "The time to send must not be in the past.");
        }

        campaign.Schedule(command.Request.At is { } at && at > now ? at : null);
        return await DtoAsync(campaign, cancellationToken);
    }

    public async Task<Result> HandleAsync(CancelMailCampaign command, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId;
        var now = (DateTimeOffset?)clock.GetUtcNow();
        var cancelled = await db.Set<MailCampaign>()
            .Where(c => c.Id == command.Id && c.TenantId == tenantId && (c.Status == MailCampaignStatus.Scheduled || c.Status == MailCampaignStatus.Sending))
            .ExecuteUpdateAsync(update => update.SetProperty(c => c.Status, MailCampaignStatus.Cancelled).SetProperty(c => c.FinishedAt, now), cancellationToken);
        if (cancelled == 0)
        {
            return await db.FindAsync(tenantId, command.Id, cancellationToken) is { } campaign ? MailCampaigns.WrongState("cancelled", campaign.Status) : MailCampaigns.NotFound;
        }

        await db.Set<OutgoingMail>().Where(m => m.CampaignId == command.Id && m.Status == OutgoingMailStatus.Queued).ExecuteDeleteAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Error?> CheckAsync(SaveMailCampaignRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 256)
        {
            return Error.Validation(nameof(SaveMailCampaignRequest.Name), "A name of up to 256 characters is required.");
        }

        if (await catalog.CheckTemplateAsync(request.TemplateName, cancellationToken) is { } template)
        {
            return template;
        }

        return request.Recipients is null
            ? Error.Validation(nameof(SaveMailCampaignRequest.Recipients), "Choose the recipients.")
            : (await directory.PreviewAsync(request.Recipients, 0, cancellationToken)).Error;
    }

    private async Task<MailCampaignDto> DtoAsync(MailCampaign campaign, CancellationToken cancellationToken) =>
        ToDto(campaign, await CountsAsync([campaign.Id], cancellationToken));

    private async Task<Dictionary<Guid, Counts>> CountsAsync(IReadOnlyCollection<Guid> campaignIds, CancellationToken cancellationToken)
    {
        var rows = await db.Set<OutgoingMail>().AsNoTracking().Where(m => m.CampaignId != null && campaignIds.Contains(m.CampaignId.Value))
            .GroupBy(m => new { m.CampaignId, m.Status }).Select(g => new { g.Key.CampaignId, g.Key.Status, Count = g.Count() }).ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.CampaignId!.Value).ToDictionary(g => g.Key, g => new Counts(
            g.Where(r => r.Status == OutgoingMailStatus.Sent).Sum(r => r.Count),
            g.Where(r => r.Status == OutgoingMailStatus.Failed).Sum(r => r.Count),
            g.Where(r => r.Status is OutgoingMailStatus.Queued or OutgoingMailStatus.Sending).Sum(r => r.Count)));
    }

    private static MailCampaignDto ToDto(MailCampaign c, Dictionary<Guid, Counts> counts)
    {
        var count = counts.GetValueOrDefault(c.Id);
        return new MailCampaignDto(
            c.Id, c.Name, c.TemplateName, RecipientSourceJson.Read(c.RecipientSource), c.Status, c.ScheduledFor, c.Total,
            count.Sent, count.Failed, count.Queued, c.CreatedAt, c.StartedAt, c.FinishedAt, c.Error);
    }

    private readonly record struct Counts(int Sent, int Failed, int Queued);
}

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

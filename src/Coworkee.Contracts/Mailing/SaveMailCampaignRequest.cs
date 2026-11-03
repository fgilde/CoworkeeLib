namespace Coworkee.Contracts.Mailing;

public sealed record SaveMailCampaignRequest(string Name, string TemplateName, RecipientSourceDto Recipients);

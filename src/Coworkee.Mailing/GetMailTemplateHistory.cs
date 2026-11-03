using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Mailing;
using Coworkee.Core.Results;

namespace Coworkee.Mailing;

/// <summary>The saved states of a template language, read from the audit trail of its overrides (also those a reset removed), newest first.</summary>
[RequiresPermission(MailPermissions.Templates.Manage)]
public sealed record GetMailTemplateHistory(string Name, string Culture) : IQuery<Result<IReadOnlyList<MailTemplateVersionDto>>>;

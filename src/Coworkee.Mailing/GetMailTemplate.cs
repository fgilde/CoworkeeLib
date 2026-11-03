using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Mailing;
using Coworkee.Core.Results;

namespace Coworkee.Mailing;

[RequiresPermission(MailPermissions.Templates.Manage)]
public sealed record GetMailTemplate(string Name, string Culture) : IQuery<Result<MailTemplateDto>>;

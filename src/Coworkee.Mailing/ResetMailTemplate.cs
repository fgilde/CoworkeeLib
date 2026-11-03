using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Mailing;
using Coworkee.Core.Results;

namespace Coworkee.Mailing;

[RequiresPermission(MailPermissions.Templates.Manage)]
public sealed record ResetMailTemplate(string Name, string Culture) : ICommand<Result>;

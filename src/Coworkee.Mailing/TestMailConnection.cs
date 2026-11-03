using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Mailing;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Results;

namespace Coworkee.Mailing;

[AiTool(Exclude = true)]
[RequiresPermission(SettingsPermissions.Manage)]
public sealed record TestMailConnection(MailConnectionTestRequest Request) : ICommand<Result<MailConnectionTestDto>>;

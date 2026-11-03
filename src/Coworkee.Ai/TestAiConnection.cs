using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Ai;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Results;

namespace Coworkee.Ai;

[AiTool(Exclude = true)]
[RequiresPermission(SettingsPermissions.Manage)]
public sealed record TestAiConnection(AiConnectionDraft Draft) : IQuery<Result<AiConnectionTestDto>>;

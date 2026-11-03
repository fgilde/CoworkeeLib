using Coworkee.Application.Authorization;
using Coworkee.Contracts.Ai;

namespace Coworkee.Ai;

internal sealed class AiPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(AiPermissions.GroupName, "AI")
            .Add(AiPermissions.Chat, "Use the assistant and the MCP server")
            .Add(AiPermissions.Audit, "View AI tool calls");
}

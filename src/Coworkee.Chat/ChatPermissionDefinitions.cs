using Coworkee.Application.Authorization;
using Coworkee.Contracts.Chat;

namespace Coworkee.Chat;

internal sealed class ChatPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(ChatPermissions.GroupName, "Chat").Add(ChatPermissions.Use, "Chat with people of the organisation");
}

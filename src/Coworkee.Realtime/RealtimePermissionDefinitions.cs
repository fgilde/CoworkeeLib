using Coworkee.Application.Authorization;
using Coworkee.Contracts.Realtime;

namespace Coworkee.Realtime;

internal sealed class RealtimePermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(RealtimePermissions.GroupName, "Realtime").Add(RealtimePermissions.ManageWatches, "Switch live watches on and off");
}

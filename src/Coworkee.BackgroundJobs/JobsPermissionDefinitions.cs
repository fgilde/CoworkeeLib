using Coworkee.Application.Authorization;
using Coworkee.Contracts.Jobs;

namespace Coworkee.BackgroundJobs;

internal sealed class JobsPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(JobsPermissions.GroupName, "Background jobs").Add(JobsPermissions.View, "View background jobs");
}

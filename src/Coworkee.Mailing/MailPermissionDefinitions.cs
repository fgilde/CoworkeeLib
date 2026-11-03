using Coworkee.Application.Authorization;
using Coworkee.Contracts.Mailing;

namespace Coworkee.Mailing;

internal sealed class MailPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(MailPermissions.GroupName, "Mail")
            .Add(MailPermissions.Templates.Manage, "Manage mail templates")
            .Add(MailPermissions.Log.View, "View mail log")
            .Add(MailPermissions.Campaigns.Send, "Send mail campaigns");
}

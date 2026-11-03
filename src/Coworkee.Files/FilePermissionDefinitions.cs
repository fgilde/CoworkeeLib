using Coworkee.Application.Authorization;
using Coworkee.Contracts.Files;

namespace Coworkee.Files;

internal sealed class FilePermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(FilePermissions.GroupName, "Files")
            .Add(FilePermissions.View, "View files")
            .Add(FilePermissions.Upload, "Upload files", FilePermissions.View)
            .Add(FilePermissions.Manage, "Manage files", FilePermissions.Upload, FilePermissions.View)
            .Add(FilePermissions.ViewRegistrations, "View registration documents of all users", FilePermissions.View);
}

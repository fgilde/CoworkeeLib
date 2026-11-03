namespace Coworkee.Application.Authorization;

public interface IPermissionDefinitionContributor
{
    void Define(PermissionDefinitionContext context);
}

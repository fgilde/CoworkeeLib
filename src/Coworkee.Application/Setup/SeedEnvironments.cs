namespace Coworkee.Application.Setup;

[Flags]
public enum SeedEnvironments
{
    Development = 1,
    Deployment = 2,
    Always = Development | Deployment,
}

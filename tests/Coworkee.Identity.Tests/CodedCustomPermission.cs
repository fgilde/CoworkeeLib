using Coworkee.Application.Authorization;

namespace Coworkee.Identity.Tests;

/// <summary>An app permission in code that uses the custom prefix: administrators can neither take its name nor change it.</summary>
public sealed class CodedCustomPermission : IPermissionDefinitionContributor
{
    public const string Name = "Custom.Coded";

    public void Define(PermissionDefinitionContext context) => context.Group("Test", "Test").Add(Name, "Coded in the app");
}

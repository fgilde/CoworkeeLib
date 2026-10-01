using System.Reflection;
using Coworkee.Application;
using Coworkee.Core.Results;
using Coworkee.Domain;
using NetArchTest.Rules;

namespace Coworkee.ArchitectureTests;

public sealed class LayerTests
{
    public static TheoryData<string, string[]> Rules() => new()
    {
        { typeof(Result).Assembly.GetName().Name!, ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Coworkee.Domain", "Coworkee.Application"] },
        { typeof(Entity).Assembly.GetName().Name!, ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Extensions", "Coworkee.Application"] },
        { typeof(IUnitOfWork).Assembly.GetName().Name!, ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Coworkee.Infrastructure"] },
    };

    [Theory]
    [MemberData(nameof(Rules))]
    public void Layer_does_not_depend_on_forbidden_namespaces(string assemblyName, string[] forbidden)
    {
        var result = Types.InAssembly(Assembly.Load(assemblyName)).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypes?.Select(t => t.FullName) ?? []));
    }
}

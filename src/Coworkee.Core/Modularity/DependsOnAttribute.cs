namespace Coworkee.Core.Modularity;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class DependsOnAttribute(params Type[] modules) : Attribute
{
    public IReadOnlyList<Type> Modules { get; } = modules;
}

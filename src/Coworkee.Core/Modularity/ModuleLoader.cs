using System.Reflection;

namespace Coworkee.Core.Modularity;

public static class ModuleLoader
{
    public static IReadOnlyList<Type> Resolve(Type rootModule)
    {
        var ordered = new List<Type>();
        var done = new HashSet<Type>();
        var visiting = new Stack<Type>();

        Visit(rootModule);
        return ordered;

        void Visit(Type type)
        {
            if (done.Contains(type))
            {
                return;
            }

            if (type.IsAbstract || !typeof(CoworkeeModule).IsAssignableFrom(type))
            {
                throw new InvalidOperationException($"'{type.FullName}' is not a concrete {nameof(CoworkeeModule)}.");
            }

            if (visiting.Contains(type))
            {
                var path = visiting.Reverse().SkipWhile(t => t != type).Append(type).Select(t => t.Name);
                throw new InvalidOperationException($"Module dependency cycle: {string.Join(" -> ", path)}");
            }

            visiting.Push(type);
            foreach (var dependency in type.GetCustomAttributes<DependsOnAttribute>().SelectMany(a => a.Modules))
            {
                Visit(dependency);
            }

            visiting.Pop();
            done.Add(type);
            ordered.Add(type);
        }
    }
}

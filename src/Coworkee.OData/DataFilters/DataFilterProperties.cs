using System.Reflection;

namespace Coworkee.OData.DataFilters;

internal static class DataFilterProperties
{
    /// <summary>The public instance property of that name declared on the most derived type: <c>GetProperty</c> throws when a derived class hides one with <c>new</c>.</summary>
    public static PropertyInfo? Find(Type type, string name) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.Name == name).OrderByDescending(p => Depth(p.DeclaringType!)).FirstOrDefault();

    private static int Depth(Type type)
    {
        var depth = 0;
        for (var t = type.BaseType; t is not null; t = t.BaseType)
        {
            depth++;
        }

        return depth;
    }
}

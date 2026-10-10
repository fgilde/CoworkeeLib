using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Routing;

namespace Coworkee.AspNetCore.ApplicationServices;

/// <summary>Filled at assembly load by the generated endpoints of each assembly that implements application services.</summary>
public static class ApplicationServiceRegistry
{
    private static readonly ConcurrentDictionary<Type, ApplicationServiceRegistration> Registrations = new();

    public static void Add<TService, TImplementation>(string route, string tag, Action<IEndpointRouteBuilder> map)
        where TService : class
        where TImplementation : class, TService =>
        Registrations[typeof(TService)] = new(typeof(TService), typeof(TImplementation), route, tag, map);

    internal static IEnumerable<ApplicationServiceRegistration> In(Assembly assembly)
    {
        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        return Registrations.Values.Where(r => r.ImplementationType.Assembly == assembly).ToList();
    }
}

internal sealed record ApplicationServiceRegistration(Type ServiceType, Type ImplementationType, string Route, string Tag, Action<IEndpointRouteBuilder> Map);

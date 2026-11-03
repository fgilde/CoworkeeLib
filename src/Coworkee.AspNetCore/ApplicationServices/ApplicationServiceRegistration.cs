using Microsoft.AspNetCore.Routing;

namespace Coworkee.AspNetCore.ApplicationServices;

internal sealed record ApplicationServiceRegistration(Type ServiceType, Type ImplementationType, string Route, string Tag, Action<IEndpointRouteBuilder> Map);

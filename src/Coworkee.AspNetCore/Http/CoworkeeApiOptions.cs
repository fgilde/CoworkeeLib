using Microsoft.AspNetCore.Routing;

namespace Coworkee.AspNetCore.Http;

public sealed class CoworkeeApiOptions
{
    private readonly List<Action<RouteGroupBuilder>> _conventions = [];

    public IReadOnlyList<Action<RouteGroupBuilder>> Conventions => _conventions;

    public CoworkeeApiOptions AddConvention(Action<RouteGroupBuilder> convention)
    {
        _conventions.Add(convention);
        return this;
    }
}

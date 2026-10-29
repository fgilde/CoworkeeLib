namespace Coworkee.OData.Tests;

/// <summary>The security suites again on <c>rt/odata</c>, whose model renames every property (lower camel case): same rules, other names.</summary>
internal static class CamelCaseRoute
{
    public const string Prefix = "rt/odata";
}

public sealed class NavigationScopeOnCamelCaseRouteTests(ODataApp app) : NavigationScopeTests(app)
{
    protected override string Route => CamelCaseRoute.Prefix;
}

public sealed class QueryCheckOnCamelCaseRouteTests(ODataApp app) : QueryCheckTests(app)
{
    protected override string Route => CamelCaseRoute.Prefix;
}

public sealed class ProtectedPropertyOnCamelCaseRouteTests(ODataApp app) : ProtectedPropertyTests(app)
{
    protected override string Route => CamelCaseRoute.Prefix;
}

public sealed class InheritedMemberOnCamelCaseRouteTests(ODataApp app) : InheritedMemberTests(app)
{
    protected override string Route => CamelCaseRoute.Prefix;
}

public sealed class GroupScopeNarrowingOnCamelCaseRouteTests(ODataApp app) : GroupScopeNarrowingTests(app)
{
    protected override string Route => CamelCaseRoute.Prefix;
}

public sealed class ChildFilterOnCamelCaseRouteTests(ODataApp app) : ChildFilterTests(app)
{
    protected override string Route => CamelCaseRoute.Prefix;
}

public sealed class EntityODataOnCamelCaseRouteTests(ODataApp app) : EntityODataTests(app)
{
    protected override string Route => CamelCaseRoute.Prefix;
}

public sealed class KeyedSetOnCamelCaseRouteTests(ODataApp app) : KeyedSetTests(app)
{
    protected override string Route => CamelCaseRoute.Prefix;
}

public sealed class ExternalDbContextOnCamelCaseRouteTests(ODataApp app) : ExternalDbContextTests(app)
{
    protected override string Route => CamelCaseRoute.Prefix;
}

public sealed class GroupRowShapeOnCamelCaseRouteTests(ODataApp app) : GroupRowShapeTests(app)
{
    protected override string Route => CamelCaseRoute.Prefix;
}

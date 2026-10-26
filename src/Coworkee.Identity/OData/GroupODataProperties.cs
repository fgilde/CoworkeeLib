using System.Linq.Expressions;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Groups;
using Coworkee.OData;
using Coworkee.OData.Computed;

namespace Coworkee.Identity.OData;

/// <summary><c>ParentPath</c> on the Groups set: the names of the ancestors joined by '/', root first ("Customer A/Hamburg"), empty for a root; exported, so the group import reads an export back.</summary>
internal sealed class GroupODataProperties : IODataComputedPropertySource
{
    public const string ParentPath = "ParentPath";

    // the name of the ancestor at a level with its separator: the group whose code is the row's code cut after that level's segment
    private static readonly Expression<Func<IQueryable<UserGroup>, string?, int, string, string>> Ancestor = (groups, code, length, separator) =>
        code!.Length > length ? separator + groups.Where(a => a.Code == code.Substring(0, length)).Select(a => a.Name).FirstOrDefault() : "";

    private static readonly Expression<Func<string, string, string>> Concat = (left, right) => left + right;

    public IEnumerable<ODataComputedProperty> Properties(ODataEntityRegistry registry)
    {
        if (registry.Find(typeof(UserGroup)) is not null)
        {
            yield return new ODataComputedProperty(typeof(UserGroup), ParentPath, typeof(string), Path) { Exported = true };
        }
    }

    // ponytail: one indexed subquery per level (15), as string aggregation over a correlated subquery does not translate on every provider (SQLite has no APPLY)
    private static Expression Path(Expression row)
    {
        var code = Expression.Property(row, nameof(UserGroup.Code));
        Expression AncestorAt(int level) => ODataComputedProperty.Inline(Ancestor, ComputedQueryRoot.Of<UserGroup>(), code,
            Expression.Constant(GroupCodes.Length(level)), Expression.Constant(level == 1 ? "" : "/"));
        var path = AncestorAt(1);
        for (var level = 2; level < GroupCodes.MaxDepth; level++)
        {
            path = ODataComputedProperty.Inline(Concat, path, AncestorAt(level));
        }

        return path;
    }
}

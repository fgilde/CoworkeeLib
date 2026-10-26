using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Groups;

/// <summary>Path codes of the group tree: 5-digit segments joined by dots, at most 16 levels (95 characters).</summary>
internal static class GroupCodes
{
    internal const int MaxDepth = 16;

    internal static string Segment(int n) => n.ToString("D5");

    internal static string Child(string parent, int n) => $"{parent}.{Segment(n)}";

    internal static int Depth(string code) => code.Count(c => c == '.') + 1;

    internal static bool IsDescendant(string code, string ancestor) => code.StartsWith(ancestor + ".", StringComparison.Ordinal);

    /// <summary>The code for a new child of <paramref name="parentId"/> (a root for null): the parent code plus the highest sibling segment plus one.</summary>
    // ponytail: two concurrent creates under one parent can pick the same code; the unique index rejects the second, which then retries by hand
    internal static async Task<string> NextAsync(DbSet<UserGroup> groups, string? parentCode, Guid? parentId, CancellationToken cancellationToken)
    {
        var codes = await groups.Where(g => g.ParentId == parentId && g.Code != null).Select(g => g.Code!).ToListAsync(cancellationToken);
        var next = codes.Select(LastSegment).DefaultIfEmpty(0).Max() + 1;
        return parentCode is null ? Segment(next) : Child(parentCode, next);
    }

    /// <summary>Gives groups that existed before the tree a code: all tenants, roots in name order after the highest existing root segment.</summary>
    public static async Task FillMissingAsync(CoworkeeDbContext db, CancellationToken cancellationToken)
    {
        var groups = db.Set<UserGroup>().IgnoreQueryFilters();
        var missing = await groups.Where(g => g.Code == null && g.ParentId == null).ToListAsync(cancellationToken);
        if (missing.Count == 0)
        {
            return;
        }

        var existing = await groups.Where(g => g.Code != null && g.ParentId == null).Select(g => new { g.TenantId, Code = g.Code! }).ToListAsync(cancellationToken);
        foreach (var tenant in missing.GroupBy(g => g.TenantId))
        {
            var next = existing.Where(g => g.TenantId == tenant.Key).Select(g => LastSegment(g.Code)).DefaultIfEmpty(0).Max();
            foreach (var group in tenant.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.Name, StringComparer.Ordinal))
            {
                group.Code = Segment(++next);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static int LastSegment(string code) => int.Parse(code[(code.LastIndexOf('.') + 1)..]);
}

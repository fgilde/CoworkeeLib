using Coworkee.Contracts.Query;

namespace Coworkee.Client.Blazor.Components.Query;

/// <summary>Structural edits of a query tree: move a node into another group, out of its group, wrap nodes in a new group, unwrap a group. Every edit respects the maximum depth.</summary>
public static class QueryTree
{
    public static QueryGroup? ParentOf(QueryGroup root, QueryNode node)
    {
        foreach (var child in root.Children)
        {
            if (child == node)
            {
                return root;
            }

            if (child is QueryGroup g && ParentOf(g, node) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>The nesting depth of a group: 0 for the root, -1 when it is not in the tree.</summary>
    public static int DepthOf(QueryGroup root, QueryGroup group)
    {
        if (root == group)
        {
            return 0;
        }

        foreach (var child in root.Children.OfType<QueryGroup>())
        {
            if (DepthOf(child, group) is var d and >= 0)
            {
                return d + 1;
            }
        }

        return -1;
    }

    /// <summary>How many group levels a node needs: 0 for a rule, 1 for a group of rules, and so on.</summary>
    public static int Levels(QueryNode node) => node is QueryGroup g ? 1 + g.Children.Select(Levels).DefaultIfEmpty(0).Max() : 0;

    /// <summary>The groups a node may move into, in tree order (not itself, not its own subtree, not where it already is, nothing deeper than <paramref name="maxDepth"/>).</summary>
    public static IEnumerable<QueryGroup> Targets(QueryGroup root, QueryNode node, int maxDepth)
    {
        var parent = ParentOf(root, node);
        return Groups(root).Where(g => g != parent && CanMove(root, node, g, maxDepth));
    }

    /// <summary>"Top level" for the root, else "Group n" by the group's place in the tree.</summary>
    public static string GroupLabel(QueryGroup root, QueryGroup group, Func<string, string> t) =>
        group == root ? t("Top level") : $"{t("Group")} {Groups(root).ToList().IndexOf(group)}";

    public static IEnumerable<QueryGroup> Groups(QueryGroup group) => new[] { group }.Concat(group.Children.OfType<QueryGroup>().SelectMany(Groups));

    public static bool CanMove(QueryGroup root, QueryNode node, QueryGroup target, int maxDepth) =>
        node != root
        && !(node is QueryGroup g && Groups(g).Contains(target))
        && DepthOf(root, target) is var depth and >= 0
        && depth + Levels(node) <= maxDepth;

    /// <summary>Moves a node into <paramref name="target"/> before <paramref name="index"/> (the end when null).</summary>
    public static bool Move(QueryGroup root, QueryNode node, QueryGroup target, int maxDepth, int? index = null)
    {
        if (!CanMove(root, node, target, maxDepth) || ParentOf(root, node) is not { } from)
        {
            return false;
        }

        var at = index ?? target.Children.Count;
        var old = from.Children.IndexOf(node);
        from.Children.RemoveAt(old);
        if (from == target && old < at)
        {
            at--;
        }

        target.Children.Insert(Math.Clamp(at, 0, target.Children.Count), node);
        return true;
    }

    public static bool MoveOut(QueryGroup root, QueryNode node, int maxDepth) =>
        ParentOf(root, node) is { } parent && ParentOf(root, parent) is { } grand
        && Move(root, node, grand, maxDepth, grand.Children.IndexOf(parent) + 1);

    /// <summary>Puts the given children of one group into a new group (joined the other way round) that takes the place of the first of them.</summary>
    public static QueryGroup? Wrap(QueryGroup root, IReadOnlyCollection<QueryNode> nodes, int maxDepth)
    {
        if (nodes.Count == 0 || ParentOf(root, nodes.First()) is not { } parent || nodes.Any(n => ParentOf(root, n) != parent))
        {
            return null;
        }

        if (DepthOf(root, parent) + 1 + nodes.Max(Levels) > maxDepth)
        {
            return null;
        }

        var ordered = parent.Children.Where(nodes.Contains).ToList();
        var wrapper = new QueryGroup { Logic = parent.Logic == QueryLogic.And ? QueryLogic.Or : QueryLogic.And, Children = ordered };
        var at = parent.Children.IndexOf(ordered[0]);
        parent.Children.RemoveAll(nodes.Contains);
        parent.Children.Insert(at, wrapper);
        return wrapper;
    }

    /// <summary>Replaces a group by its children; not for the root and not for a negated group, whose meaning would change.</summary>
    public static bool Unwrap(QueryGroup root, QueryGroup group)
    {
        if (group.Not || ParentOf(root, group) is not { } parent)
        {
            return false;
        }

        var at = parent.Children.IndexOf(group);
        parent.Children.RemoveAt(at);
        parent.Children.InsertRange(at, group.Children);
        return true;
    }
}

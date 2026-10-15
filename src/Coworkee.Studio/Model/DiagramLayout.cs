using Avalonia;
using Coworkee.Studio.ViewModels;

namespace Coworkee.Studio.Model;

/// <summary>Box sizes, reference targets and the automatic arrangement of the entity diagram.</summary>
public static class DiagramLayout
{
    public const double Width = 230;
    public const double Header = 38;
    public const double Row = 21;
    public const double Bottom = 10;
    private const double GapX = 70;
    private const double GapY = 64;
    private const int PerRow = 3;
    private const double Margin = 40;

    public static double Height(EntityViewModel entity) => Header + (Math.Max(1, entity.Properties.Count) * Row) + Bottom;

    public static IEnumerable<(ModuleViewModel Module, EntityViewModel Entity)> Entities(EntitiesStep step) =>
        step.Modules.SelectMany(m => m.Entities.Select(e => (m, e)));

    /// <summary>The entity a reference points to: Customer prefers the own module, Sales.Customer names it.</summary>
    public static EntityViewModel? Target(EntitiesStep step, ModuleViewModel module, string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var dot = reference.IndexOf('.');
        var (moduleName, name) = dot > 0 ? (reference[..dot], reference[(dot + 1)..]) : (null, reference);
        var matches = Entities(step).Where(x => x.Entity.Name == name && (moduleName is null || x.Module.Name == moduleName)).ToList();
        return (matches.FirstOrDefault(x => x.Module == module).Entity ?? (matches.Count == 1 ? matches[0].Entity : null));
    }

    /// <summary>Positions of every entity: the saved ones, the others arranged below them.</summary>
    public static Dictionary<EntityViewModel, Point> Positions(EntitiesStep step)
    {
        var placed = Entities(step).Where(x => x.Entity.Layout is not null).ToDictionary(x => x.Entity, x => new Point(x.Entity.Layout!.X, x.Entity.Layout.Y));
        if (placed.Count == Entities(step).Count())
        {
            return placed;
        }

        var offset = placed.Count == 0 ? 0 : placed.Max(p => p.Value.Y + Height(p.Key)) + GapY - Margin;
        foreach (var (entity, point) in Arrange(step).Where(p => !placed.ContainsKey(p.Key)))
        {
            placed[entity] = new Point(point.X, point.Y + offset);
        }

        return placed;
    }

    /// <summary>Rows by reference depth, referenced entities above the ones referencing them; a row holds up to three boxes.</summary>
    public static Dictionary<EntityViewModel, Point> Arrange(EntitiesStep step)
    {
        var all = Entities(step).ToList();
        var ranks = new Dictionary<EntityViewModel, int>();
        int Rank(ModuleViewModel module, EntityViewModel entity, HashSet<EntityViewModel> path)
        {
            if (ranks.TryGetValue(entity, out var known))
            {
                return known;
            }

            path.Add(entity);
            var targets = entity.Properties.Where(p => p.IsReference).Select(p => Target(step, module, p.Reference)).OfType<EntityViewModel>().Where(t => !path.Contains(t)).ToList();
            var rank = targets.Count == 0 ? 0 : targets.Max(t => Rank(all.First(x => x.Entity == t).Module, t, path) + 1);
            path.Remove(entity);
            return ranks[entity] = rank;
        }

        var positions = new Dictionary<EntityViewModel, Point>();
        var y = Margin;
        foreach (var rank in all.GroupBy(x => Rank(x.Module, x.Entity, [])).OrderBy(g => g.Key))
        {
            foreach (var row in rank.Chunk(PerRow))
            {
                foreach (var (index, (_, entity)) in row.Index())
                {
                    positions[entity] = new Point(Margin + (index * (Width + GapX)), y);
                }

                y += row.Max(x => Height(x.Entity)) + GapY;
            }
        }

        return positions;
    }
}

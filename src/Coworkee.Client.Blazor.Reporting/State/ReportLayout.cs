using Coworkee.Contracts.Reporting;

namespace Coworkee.Client.Blazor.Reporting.State;

/// <summary>The board as a reader sees it: widgets they do not get leave no gap (not while designing; the saved layout stays as it is).</summary>
public static class ReportLayout
{
    /// <summary>
    /// <paramref name="visible"/> (a subset of <paramref name="all"/>) laid out without the hidden widgets: the visible widgets of a hidden widget's row
    /// (same <c>Y</c> and <c>H</c>) share the row's span left to right, widths scaled to whole columns and the remainder to the last; then empty rows close upward.
    /// Moved widgets are copies sharing the original's query and options; the saved widgets keep their positions.
    /// </summary>
    public static IReadOnlyList<ReportWidget> ForReader(IReadOnlyList<ReportWidget> all, IReadOnlyList<ReportWidget> visible)
    {
        var hidden = all.Except(visible).ToList();
        if (hidden.Count == 0)
        {
            return visible;
        }

        var boxes = visible.ToDictionary(w => w, w => (w.X, w.Y, w.W, w.H));
        foreach (var row in all.GroupBy(w => (w.Y, w.H)).Where(r => r.Any(hidden.Contains)))
        {
            var shown = row.Where(boxes.ContainsKey).OrderBy(w => w.X).ToList();
            var (from, to) = (row.Min(w => w.X), row.Max(w => w.X + w.W));
            // another widget inside the span (e.g. a taller one between the tiles) keeps the row as it is
            if (shown.Count == 0 || visible.Except(shown).Any(w => w.X < to && w.X + w.W > from && w.Y < row.Key.Y + row.Key.H && w.Y + w.H > row.Key.Y))
            {
                continue;
            }

            var (x, width) = (from, shown.Sum(w => w.W));
            foreach (var widget in shown)
            {
                var w = widget == shown[^1] ? to - x : widget.W * (to - from) / width;
                boxes[widget] = (x, widget.Y, w, widget.H);
                x += w;
            }
        }

        // rows no visible widget covers close: each widget moves up by the empty rows above it, so none passes another
        var used = boxes.Values.SelectMany(b => Enumerable.Range(b.Y, b.H)).ToHashSet();
        return [.. visible.Select(widget =>
        {
            var (x, y, w, h) = boxes[widget];
            y -= Enumerable.Range(0, y).Count(r => !used.Contains(r));
            return (x, y, w, h) == (widget.X, widget.Y, widget.W, widget.H) ? widget : new ReportWidget
            {
                Id = widget.Id, Type = widget.Type, Title = widget.Title, Subtitle = widget.Subtitle, Query = widget.Query, Options = widget.Options, AppData = widget.AppData,
                X = x, Y = y, W = w, H = h,
            };
        })];
    }
}

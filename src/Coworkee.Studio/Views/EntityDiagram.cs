using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Coworkee.Studio.Model;
using Coworkee.Studio.ViewModels;

namespace Coworkee.Studio.Views;

/// <summary>
/// The entities as boxes with their properties and references as lines with cardinality. Drag a box to move it, drag the dot on its
/// header to another box to add a reference, drag the background to pan, use the wheel to zoom.
/// </summary>
public sealed class EntityDiagram : Control
{
    public static readonly StyledProperty<EntitiesStep?> StepProperty = AvaloniaProperty.Register<EntityDiagram, EntitiesStep?>(nameof(Step));

    private const double Handle = 6;
    private const double MinZoom = 0.3;
    private const double MaxZoom = 2.5;

    private double _zoom = 1;
    private Vector _pan;
    private bool _fitted;
    private Point _last;
    private Point _pressed;
    private EntityViewModel? _dragging;
    private Point _dragAt;
    private EntityViewModel? _linking;
    private bool _panning;

    public EntityDiagram()
    {
        ClipToBounds = true;
        Focusable = true;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public EntitiesStep? Step
    {
        get => GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public double Zoom => _zoom;

    public void ZoomBy(double factor) => ZoomAround(new Point(Bounds.Width / 2, Bounds.Height / 2), factor);

    /// <summary>Zoom and pan so that every box is visible.</summary>
    public void Fit()
    {
        FitView();
        InvalidateVisual();
    }

    private void FitView()
    {
        if (Step is null || Bounds.Width <= 0 || Content() is not { } content)
        {
            return;
        }

        // the hint below the diagram keeps its space
        var height = Bounds.Height - 44;
        _zoom = Math.Clamp(Math.Min(Bounds.Width / (content.Width + 100), height / (content.Height + 40)), MinZoom, 1.2);
        _pan = new Vector((Bounds.Width - (content.Width * _zoom)) / 2, (height - (content.Height * _zoom)) / 2) - ((Vector)content.TopLeft * _zoom);
        _fitted = true;
    }

    /// <summary>The whole diagram at full size on the surface color, for an export.</summary>
    public RenderTargetBitmap? Export()
    {
        if (Content() is not { } content)
        {
            return null;
        }

        var area = content.Inflate(30);
        var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(area.Width * 2), (int)Math.Ceiling(area.Height * 2)), new Vector(192, 192));
        using (var context = bitmap.CreateDrawingContext())
        {
            context.FillRectangle(Brush("Background"), new Rect(0, 0, area.Width, area.Height));
            using (context.PushTransform(Matrix.CreateTranslation(-area.X, -area.Y)))
            {
                Draw(context, Positions());
            }
        }

        return bitmap;
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brush("Background"), new Rect(Bounds.Size));
        if (Step is null)
        {
            return;
        }

        if (!_fitted && Bounds.Width > 0)
        {
            FitView();
        }

        DrawDots(context);
        using (context.PushTransform(Matrix.CreateScale(_zoom, _zoom) * Matrix.CreateTranslation(_pan.X, _pan.Y)))
        {
            var positions = Positions();
            Draw(context, positions);
            if (_linking is not null)
            {
                var from = positions[_linking] + new Point(DiagramLayout.Width, DiagramLayout.Header / 2);
                context.DrawLine(new Pen(Brush("Secondary"), 2, new DashStyle([4, 3], 0)), from, ToWorld(_last));
            }
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StepProperty)
        {
            if (change.OldValue is INotifyPropertyChanged old)
            {
                old.PropertyChanged -= Changed;
            }

            if (change.NewValue is INotifyPropertyChanged step)
            {
                step.PropertyChanged += Changed;
            }

            _fitted = false;
            InvalidateVisual();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Step is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _last = _pressed = e.GetPosition(this);
        var world = ToWorld(_last);
        var positions = Positions();
        var hit = HitBox(world, positions);
        if (positions.FirstOrDefault(p => Distance(world, p.Value + new Point(DiagramLayout.Width, DiagramLayout.Header / 2)) <= Handle + 4).Key is { } source)
        {
            _linking = source;
        }
        else if (hit is not null)
        {
            Step.SelectEntity(hit);
            _dragging = hit;
            _dragAt = positions[hit];
        }
        else
        {
            _panning = true;
        }

        e.Pointer.Capture(this);
        Focus();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);
        var delta = point - _last;
        _last = point;
        if (_dragging is not null)
        {
            _dragAt += delta / _zoom;
        }
        else if (_panning)
        {
            _pan += delta;
        }
        else if (_linking is null)
        {
            Cursor = HitBox(ToWorld(point), Positions()) is null ? Cursor.Default : new Cursor(StandardCursorType.SizeAll);
            return;
        }

        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var moved = Distance(_pressed, e.GetPosition(this)) > 4;
        if (_dragging is { } entity && moved)
        {
            entity.MoveTo(_dragAt.X, _dragAt.Y);
        }

        if (_linking is { } source && moved && HitBox(ToWorld(e.GetPosition(this)), Positions()) is { } target)
        {
            Step?.BeginReference(source, target);
        }

        _dragging = null;
        _linking = null;
        _panning = false;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ZoomAround(e.GetPosition(this), Math.Pow(1.1, e.Delta.Y));
        e.Handled = true;
    }

    private void ZoomAround(Point anchor, double factor)
    {
        var world = ToWorld(anchor);
        _zoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        _pan = (Vector)anchor - ((Vector)world * _zoom);
        InvalidateVisual();
    }

    private void Changed(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    private Dictionary<EntityViewModel, Point> Positions()
    {
        var positions = DiagramLayout.Positions(Step!);
        if (_dragging is not null)
        {
            positions[_dragging] = _dragAt;
        }

        return positions;
    }

    private Rect? Content()
    {
        var positions = Positions();
        return positions.Count == 0 ? null : positions.Select(p => Box(p.Key, p.Value)).Aggregate((a, b) => a.Union(b));
    }

    private void Draw(DrawingContext context, Dictionary<EntityViewModel, Point> positions)
    {
        foreach (var (module, entity) in DiagramLayout.Entities(Step!))
        {
            foreach (var (property, row) in entity.Properties.Select((p, i) => (p, i)).Where(x => x.p.IsReference))
            {
                if (DiagramLayout.Target(Step!, module, property.Reference) is { } target && positions.TryGetValue(target, out var to))
                {
                    DrawReference(context, Box(entity, positions[entity]), DiagramLayout.Header + ((row + 0.5) * DiagramLayout.Row), Box(target, to), property);
                }
            }
        }

        foreach (var (module, entity) in DiagramLayout.Entities(Step!))
        {
            DrawBox(context, module, entity, positions[entity]);
        }
    }

    private void DrawBox(DrawingContext context, ModuleViewModel module, EntityViewModel entity, Point at)
    {
        var box = Box(entity, at);
        var selected = entity == Step!.SelectedEntity;
        var border = new Pen(entity.HasErrors ? Brush("Error") : selected ? Brush("Primary") : Brush("Lines"), selected ? 2 : 1);
        context.DrawRectangle(Brush("Surface"), border, new RoundedRect(box, 10), selected ? Shadow("RaisedShadow") : Shadow("CardShadow"));
        using (context.PushClip(new RoundedRect(box, 10)))
        {
            context.FillRectangle(selected ? Brush("PrimarySoft") : Brush("SurfaceAlt"), new Rect(box.X, box.Y, box.Width, DiagramLayout.Header));
        }

        context.DrawLine(new Pen(Brush("Lines"), 1), new Point(box.X, box.Y + DiagramLayout.Header), new Point(box.Right, box.Y + DiagramLayout.Header));
        var moduleText = Text(module.Display, 11, Brush("TextSecondary"), DiagramLayout.Width / 2.5);
        context.DrawText(moduleText, new Point(box.Right - 14 - moduleText.Width, box.Y + ((DiagramLayout.Header - moduleText.Height) / 2)));
        context.DrawText(Text(entity.Display, 13.5, selected ? Brush("Primary") : Brush("Text"), DiagramLayout.Width - moduleText.Width - 34, FontWeight.SemiBold),
            new Point(box.X + 12, box.Y + 9));
        foreach (var (property, index) in entity.Properties.Select((p, i) => (p, i)))
        {
            var y = box.Y + DiagramLayout.Header + 4 + (index * DiagramLayout.Row);
            if (property.Required)
            {
                context.DrawEllipse(Brush("Primary"), null, new Point(box.X + 9, y + 8.5), 2.5, 2.5);
            }

            var type = Text(TypeText(property), 11.5, property.IsReference ? Brush("Secondary") : Brush("TextSecondary"), DiagramLayout.Width / 2);
            context.DrawText(type, new Point(box.Right - 12 - type.Width, y + 1.5));
            context.DrawText(Text(property.Name.Length > 0 ? property.Name : "?", 12.5, Brush("Text"), DiagramLayout.Width - type.Width - 36), new Point(box.X + 17, y));
        }

        context.DrawEllipse(Brush("Surface"), new Pen(Brush("Secondary"), 2), new Point(box.Right, box.Y + (DiagramLayout.Header / 2)), Handle, Handle);
    }

    // from the row of the property to the header of the target; * at the many side, 1 or 0..1 at the target.
    // Boxes above each other (and a reference to the own entity) are joined on their right sides.
    private void DrawReference(DrawingContext context, Rect source, double rowY, Rect target, PropertyViewModel property)
    {
        var stacked = Math.Abs(target.Center.X - source.Center.X) < DiagramLayout.Width;
        var right = stacked || target.Center.X >= source.Center.X;
        var start = new Point(right ? source.Right : source.X, source.Y + rowY);
        var end = stacked ? new Point(target.Right, target.Y + DiagramLayout.Header - 7) : new Point(right ? target.X : target.Right, target.Y + (DiagramLayout.Header / 2));
        var bend = stacked ? 40 + (Math.Abs(end.Y - start.Y) * 0.15) : Math.Max(40, Math.Abs(end.X - start.X) / 2);
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(start, false);
            path.CubicBezierTo(start + new Point(right ? bend : -bend, 0), end + new Point(right && !stacked ? -bend : bend, 0), end);
            path.EndFigure(false);
        }

        var brush = Brush("Secondary");
        context.DrawGeometry(null, new Pen(brush, 1.6, property.Required ? null : new DashStyle([5, 3], 0)), geometry);
        context.DrawEllipse(brush, null, end, 3.5, 3.5);
        var many = Text("*", 13, brush, 20, FontWeight.Bold);
        context.DrawText(many, start + new Point(right ? 6 : -6 - many.Width, -many.Height - 1));
        var one = Text(property.Required ? "1" : "0..1", 11.5, brush, 40, FontWeight.SemiBold);
        context.DrawText(one, end + (stacked ? new Point(8, 1) : new Point(right ? -8 - one.Width : 8, -one.Height - 2)));
    }

    private void DrawDots(DrawingContext context)
    {
        var step = 24 * _zoom;
        if (step < 8)
        {
            return;
        }

        var brush = Brush("Lines");
        for (var x = _pan.X % step; x < Bounds.Width; x += step)
        {
            for (var y = _pan.Y % step; y < Bounds.Height; y += step)
            {
                context.FillRectangle(brush, new Rect(x, y, 1.5, 1.5));
            }
        }
    }

    private static string TypeText(PropertyViewModel property) => property.Type switch
    {
        Coworkee.Cli.Spec.PropertyType.Reference => "→ " + (property.Reference ?? "?"),
        Coworkee.Cli.Spec.PropertyType.Enum => property.Enum ?? "enum",
        var type => type.ToString().ToLowerInvariant() + (property.IsText && property.MaxLength is { } max ? $"({max})" : string.Empty),
    };

    private static Rect Box(EntityViewModel entity, Point at) => new(at, new Size(DiagramLayout.Width, DiagramLayout.Height(entity)));

    private static EntityViewModel? HitBox(Point world, Dictionary<EntityViewModel, Point> positions) =>
        positions.LastOrDefault(p => Box(p.Key, p.Value).Contains(world)).Key;

    private static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    private Point ToWorld(Point point) => new((point.X - _pan.X) / _zoom, (point.Y - _pan.Y) / _zoom);

    private FormattedText Text(string text, double size, IBrush brush, double maxWidth, FontWeight weight = FontWeight.Normal) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(Avalonia.Controls.Documents.TextElement.GetFontFamily(this), FontStyle.Normal, weight), size, brush)
        {
            MaxTextWidth = Math.Max(10, maxWidth),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };

    private IBrush Brush(string key) => this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;

    private BoxShadows Shadow(string key) => this.TryFindResource(key, ActualThemeVariant, out var value) && value is BoxShadows shadows ? shadows : default;
}

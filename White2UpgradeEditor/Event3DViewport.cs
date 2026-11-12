using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace White2UpgradeEditor;

/// <summary>
/// Orbitable perspective view of Gen V zone EVENT POSITIONS. This is not a terrain/model renderer.
/// All coordinates come from ZoneEvents; no guessed map mesh is saved to the ROM.
/// </summary>
public sealed class Event3DViewport : Control
{
    public sealed record Marker(int Kind, int Index, double X, double Y, double Z);
    private readonly List<Marker> _markers = new();
    private IReadOnlyList<NsbmdMapModel.Triangle> _modelTriangles = Array.Empty<NsbmdMapModel.Triangle>();
    private double _modelScale = 1;
    private double _modelX, _modelY, _modelZ;
    public void SetModel(NsbmdMapModel.Result? model)
    {
        _modelTriangles = model?.Triangles ?? Array.Empty<NsbmdMapModel.Triangle>();
        if (_modelTriangles.Count > 0)
        {
            var points = _modelTriangles.SelectMany(t => new[] { t.A, t.B, t.C }).ToArray();
            double minX=points.Min(v=>v.X), maxX=points.Max(v=>v.X);
            double minY=points.Min(v=>v.Y), maxY=points.Max(v=>v.Y);
            double minZ=points.Min(v=>v.Z), maxZ=points.Max(v=>v.Z);
            _modelX=(minX+maxX)/2;_modelY=(minY+maxY)/2;_modelZ=(minZ+maxZ)/2;
            _modelScale=28/Math.Max(1,Math.Max(maxX-minX,Math.Max(maxY-minY,maxZ-minZ)));
        }
        InvalidateVisual();
    }
    private readonly Dictionary<Marker, Point> _hitPoints = new();
    private double _yaw = 0.7, _pitch = 0.65, _zoom = 1.0, _centerX, _centerZ;
    private Point? _lastPointer;
    private bool _dragging;
    public int SelectedKind { get; private set; } = -1;
    public int SelectedIndex { get; private set; } = -1;
    public event Action<int, int>? MarkerSelected;
    public Event3DViewport()
    {
        MinHeight = 360;
        ClipToBounds = true;
        Focusable = true;
    }
    public void SetEvents(ZoneEvents? events)
    {
        _markers.Clear();
        SelectedKind = -1; SelectedIndex = -1;
        if (events is not null)
        {
            for (int kind = 0; kind < 4; kind++)
                for (int index = 0; index < events.Counts[kind]; index++)
                {
                    var data = events.Get(kind, index);
                    if (ZoneEvents.IsRail(kind, data)) continue; // Rail coordinates are NOT world-space.
                    double x, y, z;
                    switch (kind)
                    {
                        case 0: x = ZoneEvents.I32(data, 8); y = ZoneEvents.I32(data, 12); z = ZoneEvents.I32(data, 16); break;
                        case 1: x = ZoneEvents.U16(data, 28); y = ZoneEvents.I32(data, 32); z = ZoneEvents.U16(data, 30); break;
                        case 2: x = ZoneEvents.U16(data, 8); y = ZoneEvents.U16(data, 10); z = ZoneEvents.U16(data, 12); break;
                        default: x = ZoneEvents.U16(data, 10); y = ZoneEvents.I16(data, 18); z = ZoneEvents.U16(data, 12); break;
                    }
                    _markers.Add(new Marker(kind, index, x, y, z));
                }
        }
        if (_markers.Count > 0)
        {
            _centerX = _markers.Average(m => m.X);
            _centerZ = _markers.Average(m => m.Z);
            double span = Math.Max(_markers.Max(m => m.X) - _markers.Min(m => m.X), _markers.Max(m => m.Z) - _markers.Min(m => m.Z));
            _zoom = Math.Clamp(24.0 / Math.Max(span, 1), 0.01, 4);
        }
        else { _centerX = 0; _centerZ = 0; }
        if (_markers.Count == 0) _zoom = 1;
        InvalidateVisual();
    }
    public void Select(int kind, int index)
    {
        SelectedKind = kind; SelectedIndex = index;
        InvalidateVisual();
    }
    private Point Project(double x, double y, double z)
    {
        // The game uses several native coordinate encodings. Only a spatial EVENT overview is intended.
        x = (x - _centerX) * _zoom;
        z = (z - _centerZ) * _zoom;
        y *= _zoom;
        double xx = x * Math.Cos(_yaw) - z * Math.Sin(_yaw);
        double zz = x * Math.Sin(_yaw) + z * Math.Cos(_yaw);
        double yy = y * Math.Cos(_pitch) - zz * Math.Sin(_pitch);
        double depth = y * Math.Sin(_pitch) + zz * Math.Cos(_pitch);
        double perspective = 750 / Math.Max(120, 750 + depth);
        return new Point(Bounds.Width / 2 + xx * 14 * perspective, Bounds.Height / 2 - yy * 14 * perspective);
    }
    private Point ModelPoint(NsbmdMapModel.Vertex v) => Project(_centerX + (v.X-_modelX)*_modelScale, (v.Y-_modelY)*_modelScale, _centerZ + (v.Z-_modelZ)*_modelScale);
    private static readonly IBrush[] MarkerBrushes =
    [Brushes.Gold, Brushes.CornflowerBlue, Brushes.MediumPurple, Brushes.OrangeRed];
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#101B2B")), null, Bounds);
        var grid = new Pen(new SolidColorBrush(Color.Parse("#2A4054")), 1);
        double extent = 14;
        for (int i = -14; i <= 14; i += 2)
        {
            double a = i * 2;
            context.DrawLine(grid, Project(_centerX + a, 0, _centerZ - extent * 2), Project(_centerX + a, 0, _centerZ + extent * 2));
            context.DrawLine(grid, Project(_centerX - extent * 2, 0, _centerZ + a), Project(_centerX + extent * 2, 0, _centerZ + a));
        }
        context.DrawLine(new Pen(Brushes.IndianRed, 2), Project(_centerX, 0, _centerZ), Project(_centerX + 14, 0, _centerZ));
        context.DrawLine(new Pen(Brushes.MediumSeaGreen, 2), Project(_centerX, 0, _centerZ), Project(_centerX, 0, _centerZ + 14));
        if (_modelTriangles.Count > 0)
        {
            var meshPen = new Pen(new SolidColorBrush(Color.Parse("#75B7A8")), 0.8);
            foreach (var t in _modelTriangles)
            {
                Point A = ModelPoint(t.A), B = ModelPoint(t.B), C = ModelPoint(t.C);
                context.DrawLine(meshPen,A,B);context.DrawLine(meshPen,B,C);context.DrawLine(meshPen,C,A);
            }
        }
        _hitPoints.Clear();
        foreach (var m in _markers.OrderBy(m => m.Y))
        {
            var floor = Project(m.X, 0, m.Z);
            var tip = Project(m.X, m.Y, m.Z);
            if (!double.IsFinite(tip.X) || !double.IsFinite(tip.Y)) continue;
            var brush = MarkerBrushes[m.Kind];
            context.DrawLine(new Pen(brush, 2), floor, tip);
            bool selected = m.Kind == SelectedKind && m.Index == SelectedIndex;
            context.DrawEllipse(selected ? Brushes.White : brush, selected ? new Pen(brush, 3) : null, tip, selected ? 9 : 6, selected ? 9 : 6);
            _hitPoints[m] = tip;
        }
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pos = e.GetPosition(this);
        var hit = _hitPoints.OrderBy(kv => Math.Pow(kv.Value.X - pos.X, 2) + Math.Pow(kv.Value.Y - pos.Y, 2)).FirstOrDefault();
        if (hit.Key is not null && Math.Pow(hit.Value.X - pos.X, 2) + Math.Pow(hit.Value.Y - pos.Y, 2) < 225)
        {
            Select(hit.Key.Kind, hit.Key.Index);
            MarkerSelected?.Invoke(hit.Key.Kind, hit.Key.Index);
            return;
        }
        _lastPointer = pos;
        _dragging = true;
        e.Pointer.Capture(this);
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging || _lastPointer is null) return;
        var p = e.GetPosition(this);
        _yaw += (p.X - _lastPointer.Value.X) * 0.009;
        _pitch = Math.Clamp(_pitch + (p.Y - _lastPointer.Value.Y) * 0.007, 0.15, 1.45);
        _lastPointer = p;
        InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        _lastPointer = null;
        e.Pointer.Capture(null);
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _zoom = Math.Clamp(_zoom * Math.Pow(1.15, e.Delta.Y), 0.05, 12);
        InvalidateVisual();
    }
}

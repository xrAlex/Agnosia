using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Agnosia.Controls;

public sealed class PlexusBackground : Control
{
    private const int DefaultParticleCount = 42;
    private const double DefaultConnectionDistance = 92;
    private const double DefaultNodeRadius = 2;
    private const double DefaultLineBrightness = 1.0;
    private const double DefaultNodeBrightness = 1.0;
    private const int MinimumParticleCount = 24;
    private const double MinimumParticleSpeed = 3;
    private const double MaximumParticleSpeed = 10;
    private const double EdgeOpacityFloor = 0.32;

    private static readonly Color DefaultLineColor = Color.Parse("#7AFF253A");
    private static readonly Color DefaultNodeColor = Color.Parse("#E6FF5663");
    private static readonly Color DefaultGlowColor = Color.Parse("#30FF253A");
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(66);

    public static readonly StyledProperty<IBrush?> LineBrushProperty =
        AvaloniaProperty.Register<PlexusBackground, IBrush?>(nameof(LineBrush));

    public static readonly StyledProperty<IBrush?> NodeBrushProperty =
        AvaloniaProperty.Register<PlexusBackground, IBrush?>(nameof(NodeBrush));

    public static readonly StyledProperty<IBrush?> GlowBrushProperty =
        AvaloniaProperty.Register<PlexusBackground, IBrush?>(nameof(GlowBrush));

    public static readonly StyledProperty<int> ParticleCountProperty =
        AvaloniaProperty.Register<PlexusBackground, int>(nameof(ParticleCount), DefaultParticleCount);

    public static readonly StyledProperty<double> ConnectionDistanceProperty =
        AvaloniaProperty.Register<PlexusBackground, double>(nameof(ConnectionDistance), DefaultConnectionDistance);

    public static readonly StyledProperty<double> NodeRadiusProperty =
        AvaloniaProperty.Register<PlexusBackground, double>(nameof(NodeRadius), DefaultNodeRadius);

    public static readonly StyledProperty<double> LineBrightnessProperty =
        AvaloniaProperty.Register<PlexusBackground, double>(nameof(LineBrightness), DefaultLineBrightness);

    public static readonly StyledProperty<double> NodeBrightnessProperty =
        AvaloniaProperty.Register<PlexusBackground, double>(nameof(NodeBrightness), DefaultNodeBrightness);

    private readonly DispatcherTimer _animationTimer;
    private readonly List<PlexusNode> _nodes = [];
    private readonly Random _random = new();
    private DateTime _lastFrameUtc;
    private Size _surfaceSize;

    static PlexusBackground()
    {
        AffectsRender<PlexusBackground>(
            LineBrushProperty,
            NodeBrushProperty,
            GlowBrushProperty,
            ConnectionDistanceProperty,
            NodeRadiusProperty,
            LineBrightnessProperty,
            NodeBrightnessProperty);
    }

    public PlexusBackground()
    {
        ClipToBounds = true;
        IsHitTestVisible = false;

        _animationTimer = new DispatcherTimer
        {
            Interval = FrameInterval
        };
        _animationTimer.Tick += OnAnimationFrame;

        AttachedToVisualTree += (_, _) =>
        {
            _lastFrameUtc = DateTime.UtcNow;
            EnsureNodes(forceReset: _nodes.Count == 0);
            _animationTimer.Start();
        };

        DetachedFromVisualTree += (_, _) => _animationTimer.Stop();
    }

    public IBrush? LineBrush
    {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public IBrush? NodeBrush
    {
        get => GetValue(NodeBrushProperty);
        set => SetValue(NodeBrushProperty, value);
    }

    public IBrush? GlowBrush
    {
        get => GetValue(GlowBrushProperty);
        set => SetValue(GlowBrushProperty, value);
    }

    public int ParticleCount
    {
        get => GetValue(ParticleCountProperty);
        set => SetValue(ParticleCountProperty, value);
    }

    public double ConnectionDistance
    {
        get => GetValue(ConnectionDistanceProperty);
        set => SetValue(ConnectionDistanceProperty, value);
    }

    public double NodeRadius
    {
        get => GetValue(NodeRadiusProperty);
        set => SetValue(NodeRadiusProperty, value);
    }

    public double LineBrightness
    {
        get => GetValue(LineBrightnessProperty);
        set => SetValue(LineBrightnessProperty, value);
    }

    public double NodeBrightness
    {
        get => GetValue(NodeBrightnessProperty);
        set => SetValue(NodeBrightnessProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BoundsProperty)
        {
            ResizeNodes(Bounds.Size);
            return;
        }

        if (change.Property == ParticleCountProperty)
        {
            EnsureNodes(forceReset: true);
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (_nodes.Count == 0 || Bounds.Width <= 1 || Bounds.Height <= 1)
        {
            return;
        }

        var lineColor = ResolveColor(LineBrush, DefaultLineColor);
        var nodeColor = ResolveColor(NodeBrush, DefaultNodeColor);
        var glowColor = ResolveColor(GlowBrush, DefaultGlowColor);
        var connectionDistance = Math.Max(20, ConnectionDistance);
        var nodeRadius = Math.Max(0.8, NodeRadius);
        var lineBrightness = Math.Clamp(LineBrightness, 0, 4);
        var nodeBrightness = Math.Clamp(NodeBrightness, 0, 4);
        var bounds = Bounds.Size;
        var nodeCount = _nodes.Count;
        var connectionDistanceSquared = connectionDistance * connectionDistance;
        var linePen = new Pen(new SolidColorBrush(ScaleColorBrightness(lineColor, lineBrightness)), 1.15);
        var glowBrush = new SolidColorBrush(ScaleColorBrightness(glowColor, nodeBrightness));
        var nodeBrush = new SolidColorBrush(ScaleColorBrightness(nodeColor, nodeBrightness));

        for (var index = 0; index < nodeCount; index++)
        {
            var source = _nodes[index];

            for (var otherIndex = index + 1; otherIndex < nodeCount; otherIndex++)
            {
                var target = _nodes[otherIndex];
                var deltaX = source.Position.X - target.Position.X;
                var deltaY = source.Position.Y - target.Position.Y;
                var distanceSquared = deltaX * deltaX + deltaY * deltaY;
                if (distanceSquared > connectionDistanceSquared)
                {
                    continue;
                }

                var distance = Math.Sqrt(distanceSquared);
                var distanceFactor = 1 - (distance / connectionDistance);
                var edgeFactor = GetEdgeFactor((source.Position.Y + target.Position.Y) * 0.5, bounds.Height);
                var opacity = Math.Clamp(Math.Pow(distanceFactor, 1.35) * edgeFactor * 0.9, 0, 1);
                using (context.PushOpacity(opacity))
                {
                    context.DrawLine(linePen, source.Position, target.Position);
                }
            }
        }

        foreach (var node in _nodes)
        {
            var pulse = 0.5 + Math.Sin(node.Phase) * 0.5;
            var edgeFactor = GetEdgeFactor(node.Position.Y, bounds.Height);
            var glowRadius = nodeRadius * (2.6 + pulse * 0.7);
            var dotRadius = nodeRadius + pulse * 0.45;
            var glowOpacity = Math.Clamp((0.24 + pulse * 0.18) * edgeFactor, 0, 1);
            var dotOpacity = Math.Clamp((0.72 + pulse * 0.24) * edgeFactor, 0, 1);

            using (context.PushOpacity(glowOpacity))
            {
                context.DrawEllipse(
                    glowBrush,
                    null,
                    node.Position,
                    glowRadius,
                    glowRadius);
            }

            using (context.PushOpacity(dotOpacity))
            {
                context.DrawEllipse(
                    nodeBrush,
                    null,
                    node.Position,
                    dotRadius,
                    dotRadius);
            }
        }
    }

    private void OnAnimationFrame(object? sender, EventArgs e)
    {
        EnsureNodes();

        if (_nodes.Count == 0 || Bounds.Width <= 1 || Bounds.Height <= 1)
        {
            return;
        }

        var currentFrameUtc = DateTime.UtcNow;
        var elapsedSeconds = Math.Clamp((currentFrameUtc - _lastFrameUtc).TotalSeconds, 0.01, 0.05);
        _lastFrameUtc = currentFrameUtc;

        var width = Bounds.Width;
        var height = Bounds.Height;
        for (var index = 0; index < _nodes.Count; index++)
        {
            var node = _nodes[index];
            var nextPosition = node.Position + (node.Velocity * elapsedSeconds);

            if (nextPosition.X <= 0 || nextPosition.X >= width)
            {
                node.Velocity = new Vector(-node.Velocity.X, node.Velocity.Y);
                nextPosition = new Point(Math.Clamp(nextPosition.X, 0, width), nextPosition.Y);
            }

            if (nextPosition.Y <= 0 || nextPosition.Y >= height)
            {
                node.Velocity = new Vector(node.Velocity.X, -node.Velocity.Y);
                nextPosition = new Point(nextPosition.X, Math.Clamp(nextPosition.Y, 0, height));
            }

            node.Position = nextPosition;
            node.Phase += elapsedSeconds * node.PulseSpeed;
        }

        InvalidateVisual();
    }

    private void EnsureNodes(bool forceReset = false)
    {
        if (Bounds.Width <= 1 || Bounds.Height <= 1)
        {
            return;
        }

        var particleCount = GetParticleCount();
        if (!forceReset && _nodes.Count == particleCount)
        {
            return;
        }

        _nodes.Clear();
        _surfaceSize = Bounds.Size;

        for (var index = 0; index < particleCount; index++)
        {
            var angle = _random.NextDouble() * Math.Tau;
            var speed = MinimumParticleSpeed + (_random.NextDouble() * (MaximumParticleSpeed - MinimumParticleSpeed));
            _nodes.Add(new PlexusNode
            {
                Position = new Point(_random.NextDouble() * Bounds.Width, _random.NextDouble() * Bounds.Height),
                Velocity = new Vector(Math.Cos(angle) * speed, Math.Sin(angle) * speed),
                Phase = _random.NextDouble() * Math.Tau,
                PulseSpeed = 0.7 + (_random.NextDouble() * 0.9)
            });
        }

        _lastFrameUtc = DateTime.UtcNow;
        InvalidateVisual();
    }

    private void ResizeNodes(Size newSize)
    {
        if (newSize.Width <= 1 || newSize.Height <= 1)
        {
            return;
        }

        if (_nodes.Count != GetParticleCount() || _surfaceSize.Width <= 1 || _surfaceSize.Height <= 1)
        {
            _surfaceSize = newSize;
            EnsureNodes(forceReset: true);
            return;
        }

        var scaleX = newSize.Width / _surfaceSize.Width;
        var scaleY = newSize.Height / _surfaceSize.Height;
        if (Math.Abs(scaleX - 1) <= 0.015 && Math.Abs(scaleY - 1) <= 0.015)
        {
            _surfaceSize = newSize;
            return;
        }

        foreach (var node in _nodes)
        {
            node.Position = new Point(node.Position.X * scaleX, node.Position.Y * scaleY);
        }

        _surfaceSize = newSize;
        InvalidateVisual();
    }

    private int GetParticleCount()
    {
        return Math.Max(MinimumParticleCount, ParticleCount);
    }

    private static double GetEdgeFactor(double y, double height)
    {
        if (height <= 1)
        {
            return 1;
        }

        var normalized = y / height;
        var centerDistance = Math.Abs((normalized - 0.5) * 2);
        return EdgeOpacityFloor + ((1 - EdgeOpacityFloor) * centerDistance);
    }

    private static Color ResolveColor(IBrush? brush, Color fallback)
    {
        if (brush is not ISolidColorBrush solidColorBrush)
        {
            return fallback;
        }

        return ApplyOpacity(solidColorBrush.Color, solidColorBrush.Opacity);
    }

    private static Color ApplyOpacity(Color color, double opacity)
    {
        var alpha = (byte)Math.Clamp(Math.Round(color.A * opacity), 0, byte.MaxValue);
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    private static Color ScaleColorBrightness(Color color, double brightness)
    {
        var alpha = (byte)Math.Clamp(Math.Round(color.A * brightness), 0, byte.MaxValue);
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    private sealed class PlexusNode
    {
        public Point Position { get; set; }

        public Vector Velocity { get; set; }

        public double Phase { get; set; }

        public double PulseSpeed { get; set; }
    }
}

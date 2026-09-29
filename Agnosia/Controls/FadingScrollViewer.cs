using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Agnosia.Controls;

/// <summary>Fades only scrollable content that continues beyond a vertical viewport edge.</summary>
public sealed class FadingScrollViewer : ScrollViewer
{
    private const double FadeLength = 10;
    private static readonly double[] Ramp = [0, 0.15625, 0.5, 0.84375, 1];
    private readonly LinearGradientBrush _mask = new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative)
    };
    private ScrollContentPresenter? _presenter;

    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    public FadingScrollViewer()
    {
        for (var i = 0; i < 10; i++)
            _mask.GradientStops.Add(new GradientStop(Colors.Black, 0));
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_presenter is not null)
        {
            _presenter.SizeChanged -= OnPresenterSizeChanged;
            _presenter.OpacityMask = null;
        }

        base.OnApplyTemplate(e);
        _presenter = e.NameScope.Find<ScrollContentPresenter>("PART_ContentPresenter");
        if (_presenter is not null)
            _presenter.SizeChanged += OnPresenterSizeChanged;
        UpdateMask();
    }

    protected override void OnScrollChanged(ScrollChangedEventArgs e)
    {
        base.OnScrollChanged(e);
        UpdateMask();
    }

    private void OnPresenterSizeChanged(object? sender, SizeChangedEventArgs e) => UpdateMask();

    private void UpdateMask()
    {
        if (_presenter is null) return;

        var height = _presenter.Bounds.Height;
        var scrollableHeight = Extent.Height - Viewport.Height;
        if (height <= 0 || Viewport.Height <= 0 || scrollableHeight <= 0)
        {
            _presenter.OpacityMask = null;
            return;
        }

        var fraction = Math.Min(FadeLength, height / 2) / height;
        var topStrength = Math.Clamp(Offset.Y / FadeLength, 0, 1);
        var bottomStrength = Math.Clamp((scrollableHeight - Offset.Y) / FadeLength, 0, 1);
        for (var i = 0; i < Ramp.Length; i++)
        {
            var top = _mask.GradientStops[i];
            top.Offset = fraction * i / 4;
            top.Color = Color.FromArgb((byte)Math.Round(255 * (1 - topStrength * (1 - Ramp[i]))), 0, 0, 0);
            var bottom = _mask.GradientStops[9 - i];
            bottom.Offset = 1 - fraction * i / 4;
            bottom.Color = Color.FromArgb((byte)Math.Round(255 * (1 - bottomStrength * (1 - Ramp[i]))), 0, 0, 0);
        }

        _presenter.OpacityMask = _mask;
    }
}

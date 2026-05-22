using Agnosia.Infrastructure;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace Agnosia.Controls;

public partial class WatcherEye : UserControl
{
    private const double MaxPupilOffsetX = 7.4;
    private const double MaxPupilOffsetY = 4.2;
    private const double GazeVerticalWeight = 0.58;
    private const double FullGazeDistance = 300;
    private const double PupilFollowStep = 0.2;
    private const double IrisHorizontalSqueeze = 0.24;
    private const double IrisVerticalSqueeze = 0.12;
    private const double PupilHorizontalSqueeze = 0.4;
    private const double PupilVerticalSqueeze = 0.15;
    private const double HighlightMaxOffsetX = 0.3;
    private const double HighlightMaxOffsetY = 0.1;
    private const double PupilStopThreshold = 0.1;
    private const RoutingStrategies PointerRoutingStrategies = RoutingStrategies.Tunnel | RoutingStrategies.Bubble;
    private static readonly TimeSpan ReturnToUserDelay = TimeSpan.FromMilliseconds(650);
    private static readonly TimeSpan PointerIdleReturnDelay = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan PupilFrameInterval = TimeSpan.FromMilliseconds(16);

    private readonly DispatcherTimer _pupilMotionTimer;
    private readonly DispatcherTimer _pointerIdleTimer;
    private readonly DispatcherTimer _returnToUserTimer;
    private readonly TranslateTransform _pupilTransform;
    private readonly TranslateTransform _pupilHighlightTransform;
    private readonly ScaleTransform _irisDiscScaleTransform;
    private readonly ScaleTransform _pupilCoreScaleTransform;
    private TopLevel? _pointerSource;
    private bool _isTrackingPointer;
    private bool _isAttachedToPointerEvents;
    private double _targetPupilX;
    private double _targetPupilY;

    public WatcherEye()
    {
        InitializeComponent();

        _pupilTransform = Pupil.RenderTransform as TranslateTransform
                          ?? throw new InvalidOperationException("Watcher pupil transform was not found.");
        _pupilHighlightTransform = PupilHighlight.RenderTransform as TranslateTransform
                                   ?? throw new InvalidOperationException(
                                       "Watcher pupil highlight transform was not found.");
        _irisDiscScaleTransform = IrisDisc.RenderTransform as ScaleTransform
                                  ?? throw new InvalidOperationException("Watcher iris scale transform was not found.");
        _pupilCoreScaleTransform = PupilCore.RenderTransform as ScaleTransform
                                   ?? throw new InvalidOperationException(
                                       "Watcher pupil scale transform was not found.");

        _pupilMotionTimer = new DispatcherTimer
        {
            Interval = PupilFrameInterval
        };
        _pupilMotionTimer.Tick += (_, _) => UpdatePupilPosition();

        _pointerIdleTimer = new DispatcherTimer
        {
            Interval = PointerIdleReturnDelay
        };
        _pointerIdleTimer.Tick += (_, _) =>
        {
            _pointerIdleTimer.Stop();
            if (_isTrackingPointer)
            {
                _isTrackingPointer = false;
                ScheduleLookAtUser();
            }
        };

        _returnToUserTimer = new DispatcherTimer
        {
            Interval = ReturnToUserDelay
        };
        _returnToUserTimer.Tick += (_, _) =>
        {
            _returnToUserTimer.Stop();
            SetPupilTarget(0, 0);
        };

        AttachedToVisualTree += (_, _) => AttachPointerSource();
        DetachedFromVisualTree += (_, _) => DetachPointerSource();
    }

    private void AttachPointerSource()
    {
        DetachPointerSource();
        _pointerSource = TopLevel.GetTopLevel(this);
        if (_pointerSource is null) return;

        _pointerSource.AddHandler(PointerPressedEvent, OnPointerPressed, PointerRoutingStrategies, true);
        _pointerSource.AddHandler(PointerMovedEvent, OnPointerMoved, PointerRoutingStrategies, true);
        _pointerSource.AddHandler(PointerReleasedEvent, OnPointerReleased, PointerRoutingStrategies, true);
        _pointerSource.AddHandler(PointerCaptureLostEvent, OnPointerCaptureLost, PointerRoutingStrategies, true);
        _pointerSource.AddHandler(PointerExitedEvent, OnPointerExited, PointerRoutingStrategies, true);
        WatcherPointerTracker.PointerChanged += OnPlatformPointerChanged;
        _isAttachedToPointerEvents = true;
    }

    private void DetachPointerSource()
    {
        if (_isAttachedToPointerEvents && _pointerSource is not null)
        {
            WatcherPointerTracker.PointerChanged -= OnPlatformPointerChanged;
            _pointerSource.RemoveHandler(PointerPressedEvent, OnPointerPressed);
            _pointerSource.RemoveHandler(PointerMovedEvent, OnPointerMoved);
            _pointerSource.RemoveHandler(PointerReleasedEvent, OnPointerReleased);
            _pointerSource.RemoveHandler(PointerCaptureLostEvent, OnPointerCaptureLost);
            _pointerSource.RemoveHandler(PointerExitedEvent, OnPointerExited);
        }

        _pointerSource = null;
        _isAttachedToPointerEvents = false;
        _pupilMotionTimer.Stop();
        _pointerIdleTimer.Stop();
        _returnToUserTimer.Stop();
        _isTrackingPointer = false;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginOrMoveTracking(e.GetPosition(this));
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var pointerPoint = e.GetCurrentPoint(this);
        var shouldTrack =
            _isTrackingPointer ||
            pointerPoint.Properties.IsLeftButtonPressed ||
            e.Pointer.Type is PointerType.Touch or PointerType.Pen;

        if (!shouldTrack) return;

        BeginOrMoveTracking(pointerPoint.Position);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        EndTracking();
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (IsInsidePointerSource(e)) return;

        EndTracking();
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        ScheduleIdleReturn();
    }

    private void OnPlatformPointerChanged(object? sender, WatcherPointerEvent e)
    {
        if (e.State == WatcherPointerState.Release)
        {
            EndTracking();
            return;
        }

        BeginOrMoveTrackingFromRoot(e.RootPosition);
    }

    private bool IsInsidePointerSource(PointerEventArgs e)
    {
        var source = _pointerSource;
        return source is not null && new Rect(source.Bounds.Size).Contains(e.GetPosition(source));
    }

    private void BeginOrMoveTrackingFromRoot(Point rootPosition)
    {
        var source = _pointerSource;
        var localPosition = source?.TranslatePoint(rootPosition, this);
        if (localPosition is null) return;

        BeginOrMoveTracking(localPosition.Value);
    }

    private void BeginOrMoveTracking(Point target)
    {
        _returnToUserTimer.Stop();
        _isTrackingPointer = true;
        LookAt(target);
        ScheduleIdleReturn();
    }

    private void EndTracking()
    {
        if (!_isTrackingPointer) return;

        _pointerIdleTimer.Stop();
        _isTrackingPointer = false;
        ScheduleLookAtUser();
    }

    private void LookAt(Point target)
    {
        var eyeCenter = Iris.TranslatePoint(
            new Point(Iris.Bounds.Width / 2, Iris.Bounds.Height / 2),
            this);

        if (eyeCenter is null)
        {
            SetPupilTarget(0, 0);
            return;
        }

        MovePupil(target.X - eyeCenter.Value.X, target.Y - eyeCenter.Value.Y);
    }

    private void ScheduleIdleReturn()
    {
        if (!_isTrackingPointer) return;

        _pointerIdleTimer.Stop();
        _pointerIdleTimer.Start();
    }

    private void ScheduleLookAtUser(bool immediate = false)
    {
        _returnToUserTimer.Stop();
        if (immediate)
        {
            SetPupilTarget(0, 0);
            return;
        }

        _returnToUserTimer.Start();
    }

    private void MovePupil(double deltaX, double deltaY)
    {
        var weightedY = deltaY * GazeVerticalWeight;
        var vectorLength = Math.Sqrt(deltaX * deltaX + weightedY * weightedY);
        if (vectorLength <= 0.01)
        {
            SetPupilTarget(0, 0);
            return;
        }

        var rawDistance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        var strength = Math.Clamp(rawDistance / FullGazeDistance, 0, 1);

        SetPupilTarget(
            deltaX / vectorLength * MaxPupilOffsetX * strength,
            weightedY / vectorLength * MaxPupilOffsetY * strength);
    }

    private void SetPupilTarget(double x, double y)
    {
        if (!_pupilMotionTimer.IsEnabled
            && Math.Abs(_targetPupilX - x) <= PupilStopThreshold
            && Math.Abs(_targetPupilY - y) <= PupilStopThreshold
            && Math.Abs(_pupilTransform.X - x) <= PupilStopThreshold
            && Math.Abs(_pupilTransform.Y - y) <= PupilStopThreshold)
            return;

        _targetPupilX = x;
        _targetPupilY = y;

        if (!_pupilMotionTimer.IsEnabled) _pupilMotionTimer.Start();
    }

    private void UpdatePupilPosition()
    {
        var deltaX = _targetPupilX - _pupilTransform.X;
        var deltaY = _targetPupilY - _pupilTransform.Y;

        if (Math.Abs(deltaX) <= PupilStopThreshold && Math.Abs(deltaY) <= PupilStopThreshold)
        {
            _pupilTransform.X = _targetPupilX;
            _pupilTransform.Y = _targetPupilY;
            UpdatePupilPerspective();
            _pupilMotionTimer.Stop();
            return;
        }

        _pupilTransform.X += deltaX * PupilFollowStep;
        _pupilTransform.Y += deltaY * PupilFollowStep;
        UpdatePupilPerspective();
    }

    private void UpdatePupilPerspective()
    {
        var gazeX = Math.Clamp(_pupilTransform.X / MaxPupilOffsetX, -1, 1);
        var gazeY = Math.Clamp(_pupilTransform.Y / MaxPupilOffsetY, -1, 1);
        var intensity = Math.Clamp(Math.Sqrt(gazeX * gazeX + gazeY * gazeY), 0, 1);

        if (intensity <= 0.01)
        {
            _irisDiscScaleTransform.ScaleX = 1;
            _irisDiscScaleTransform.ScaleY = 1;
            _pupilCoreScaleTransform.ScaleX = 1;
            _pupilCoreScaleTransform.ScaleY = 1;
            _pupilHighlightTransform.X = 0;
            _pupilHighlightTransform.Y = 0;
            return;
        }

        _irisDiscScaleTransform.ScaleX = 1 - Math.Abs(gazeX) * IrisHorizontalSqueeze;
        _irisDiscScaleTransform.ScaleY = 1 - Math.Abs(gazeY) * IrisVerticalSqueeze;
        _pupilCoreScaleTransform.ScaleX = 1 - Math.Abs(gazeX) * PupilHorizontalSqueeze;
        _pupilCoreScaleTransform.ScaleY = 1 - Math.Abs(gazeY) * PupilVerticalSqueeze;
        _pupilHighlightTransform.X = -gazeX * HighlightMaxOffsetX;
        _pupilHighlightTransform.Y = -gazeY * HighlightMaxOffsetY;
    }
}

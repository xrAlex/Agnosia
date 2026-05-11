using Avalonia;

namespace Agnosia.Infrastructure;

public enum WatcherPointerState
{
    Move,
    Release
}

public sealed record WatcherPointerEvent(Point RootPosition, WatcherPointerState State);

public static class WatcherPointerTracker
{
    public static event EventHandler<WatcherPointerEvent>? PointerChanged;

    public static void Move(Point rootPosition) =>
        PointerChanged?.Invoke(null, new WatcherPointerEvent(rootPosition, WatcherPointerState.Move));

    public static void Release(Point rootPosition) =>
        PointerChanged?.Invoke(null, new WatcherPointerEvent(rootPosition, WatcherPointerState.Release));
}

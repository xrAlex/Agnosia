using Agnosia.Services;

namespace Agnosia.Android.Services;

internal sealed record HiddenAppUsageEvent(long Timestamp, int Type, string? PackageName, string? ClassName);

// One instance per launch. Only the latest relevant events are retained, never usage history.
internal sealed class HiddenAppUsageObservationReducer(string packageName, DateTimeOffset startedAt)
{
    private HiddenAppUsageEvent? _target;
    private HiddenAppUsageEvent? _foreground;
    private HiddenAppUsageEvent? _targetResume;
    private readonly Dictionary<string, long> _activityStoppedAt = new(StringComparer.Ordinal);
    private long _targetPausedAt;
    private long? _delegatedForegroundAt;
    private bool _sawTarget;

    public SessionObservation Reduce(IEnumerable<HiddenAppUsageEvent>? events)
    {
        if (events is null) return new(false, null, false, null, _sawTarget, false);

        foreach (var item in events.OrderBy(item => item.Timestamp))
        {
            if (item.Timestamp < startedAt.ToUnixTimeMilliseconds()) continue;
            if (HiddenAppUsageEventPolicy.IsForeground(item.Type))
            {
                if (_foreground is null || item.Timestamp >= _foreground.Timestamp)
                {
                    _foreground = item;
                    if (_target is { } target && HiddenAppUsageEventPolicy.CanStartDelegatedFlow(target.Type)
                        && item.Timestamp >= target.Timestamp && item.Timestamp - target.Timestamp <= 15000
                        && IsSystemDelegatedFlow(item.PackageName, item.ClassName))
                        _delegatedForegroundAt = item.Timestamp;
                }
                if (item.PackageName == packageName)
                {
                    _sawTarget = true;
                    if (_targetResume is null || item.Timestamp > _targetResume.Timestamp)
                    {
                        _targetResume = item;
                    }
                }
            }

            if (item.PackageName == packageName)
            {
                if (item.Type == 2) _targetPausedAt = Math.Max(_targetPausedAt, item.Timestamp);
                if (HiddenAppUsageEventPolicy.IsConfirmedInvisible(item.Type))
                {
                    var activity = item.ClassName ?? string.Empty;
                    _activityStoppedAt[activity] = Math.Max(_activityStoppedAt.GetValueOrDefault(activity), item.Timestamp);
                    if (_activityStoppedAt.Count > 64)
                        _activityStoppedAt.Remove(_activityStoppedAt.MinBy(pair => pair.Value).Key);
                }
            }

            if (item.PackageName == packageName && HiddenAppUsageEventPolicy.IsLifecycleTransition(item.Type)
                && (_target is null || item.Timestamp >= _target.Timestamp)) _target = item;
        }

        if (_target is null) return new(false, _foreground?.PackageName, false, null, _sawTarget, false);
        if (HiddenAppUsageEventPolicy.IsForeground(_target.Type))
            return new(true, packageName, false, null, true, false);

        // A late stop of the old Activity must not hide a different, newly resumed Activity.
        if (_targetResume is { } resumed
            && _activityStoppedAt.GetValueOrDefault(resumed.ClassName ?? string.Empty) < resumed.Timestamp
            && resumed.ClassName != _target.ClassName
            && !string.IsNullOrEmpty(resumed.ClassName) && _foreground?.PackageName == packageName)
            return new(true, packageName, false, null, true, false);

        var stopped = HiddenAppUsageEventPolicy.IsConfirmedInvisible(_target.Type);
        DateTimeOffset? inactiveSince = stopped ? DateTimeOffset.FromUnixTimeMilliseconds(_target.Timestamp) : null;
        var delegated = _foreground is { } foreground
                        && IsSystemDelegatedFlow(foreground.PackageName, foreground.ClassName)
                        && (_delegatedForegroundAt == foreground.Timestamp
                            || foreground.Timestamp >= _target.Timestamp
                            && foreground.Timestamp - _target.Timestamp <= 15000
                            || _targetPausedAt > 0 && _targetPausedAt >= (_targetResume?.Timestamp ?? 0)
                            && foreground.Timestamp >= _targetPausedAt && foreground.Timestamp - _targetPausedAt <= 15000);
        if (delegated)
        {
            _delegatedForegroundAt = _foreground!.Timestamp;
            return new(false, _foreground?.PackageName, false, inactiveSince, _sawTarget, true);
        }
        _delegatedForegroundAt = null;
        if (IsTransientSystemPackage(_foreground?.PackageName))
            return new(true, _foreground?.PackageName, false, null, _sawTarget, false);

        return new(false, _foreground?.PackageName, stopped, inactiveSince, _sawTarget, false);
    }

    private static bool IsTransientSystemPackage(string? packageName) => packageName is
        "com.google.android.permissioncontroller" or "com.android.permissioncontroller" or "com.google.android.gms";

    private static bool IsSystemDelegatedFlow(string? packageName, string? className) => packageName is
        "com.android.settings" or "com.google.android.permissioncontroller" or "com.android.permissioncontroller"
        or "com.android.packageinstaller" or "com.google.android.documentsui" or "com.android.documentsui"
        || className is not null && (className.Contains("AppNotificationSettingsActivity", StringComparison.Ordinal)
            || className.Contains("Permission", StringComparison.Ordinal)
            || className.Contains("PackageInstaller", StringComparison.Ordinal)
            || className.Contains("DocumentsActivity", StringComparison.Ordinal));
}

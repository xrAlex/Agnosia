using System.Globalization;
using Agnosia.Android.Activities;
using Agnosia.Android.Api.Commands;
using Agnosia.Android.Api.Gateways;
using Agnosia.Android.Api.Platform;
using Agnosia.Android.Api.Storage;
using Agnosia.Android.Receivers;
using Agnosia.Android.Services;
using Agnosia.Infrastructure;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Avalonia.Android;
using Avalonia.Controls;
using AvaloniaPoint = Avalonia.Point;
using JavaSystem = Java.Lang.JavaSystem;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android;

[Activity(
    Label = "@string/app_name",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@mipmap/ic_launcher",
    MainLauncher = true,
    Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity, IAndroidActivityHost
{
    private const string LogTag = "AgnosiaMainActivity";
    private const int MaxPendingActivityStarts = 8;
    private const int MaxActivityStartsPerDrain = 2;
    private const long PendingDrainDelayMilliseconds = 150;
    private static readonly Lock RequestSync = new();
    private static readonly Dictionary<int, TaskCompletionSource<AndroidActivityResult>> PendingResults = [];
    private static readonly Queue<ActivityStartRequest> PendingActivityStarts = [];
    private static int _nextRequestCode = 4100;
    private static bool _startupMitigationsApplied;
    private bool _isResumed;
    private bool _pendingDrainScheduled;
    private long _lastPublishedMoveAtMilliseconds;

    private static MainActivity? Current { get; set; }

    static MainActivity()
    {
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            global::Android.Util.Log.Error(LogTag, $"Unhandled exception: {args.ExceptionObject}");
        };
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        if (IsProfileOwnerStartup())
        {
            ServiceRegistry.SuppressPrimaryUiStartup = true;
            base.OnCreate(savedInstanceState);
            BootstrapWorkProfileAndFinish();
            return;
        }

        ApplyStartupMitigations();

        InitializePrimaryProfileStartup();

        base.OnCreate(savedInstanceState);

        StartBackgroundInitialization();
    }

    private void InitializePrimaryProfileStartup()
    {
        AgnosiaRuntime.Initialize(this);
        ServiceRegistry.SuppressPrimaryUiStartup = false;
        ServiceRegistry.PlatformBridge = AndroidPlatformBridge.Instance;
        ServiceRegistry.InitialTheme = AndroidSettingsStore.LoadAppTheme(LocalStorageManager.Instance);
        AndroidPlatformBridge.Instance.AttachActivity(this);
        Current = this;
    }

    private void StartBackgroundInitialization()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(100);

                RunOnUiThread(() => ApplyPreferredDisplayMode());
                WorkProfileLockFreezeService.EnsureRunning(this);

                if (!AgnosiaUtilities.IsProfileOwner(this)) return;

                RunOnUiThread(BootstrapWorkProfileAndFinish);
            }
            catch (Exception exception)
            {
                Log.Error(LogTag, $"Background initialization failed: {exception}");
            }
        });
    }

    private bool IsProfileOwnerStartup()
    {
        try
        {
            return AgnosiaUtilities.IsProfileOwner(this);
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Warn(LogTag, $"Profile-owner startup check failed: {exception.Message}");
            return false;
        }
    }

    private void BootstrapWorkProfileAndFinish()
    {
        try
        {
            AgnosiaRuntime.Initialize(this);
            AgnosiaUtilities.EnforceWorkProfilePolicies(
                this,
                typeof(AgnosiaDeviceAdminReceiver),
                typeof(MainActivity),
                true);
            AgnosiaUtilities.EnforceUserRestrictions(this, typeof(AgnosiaDeviceAdminReceiver));
            WorkProfileLockFreezeService.EnsureRunning(this);
            Log.Info(LogTag, "Work-profile MainActivity bootstrap completed; finishing without primary UI.");
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"Work-profile MainActivity bootstrap failed: {exception}");
        }
        finally
        {
            Finish();
        }
    }

    protected override void OnResume()
    {
        base.OnResume();
        _isResumed = true;
        Current = this;
        AndroidPlatformBridge.Instance.AttachActivity(this);
        ServiceRegistry.NotifyPrimaryActivityResumed();
        ApplyPreferredDisplayMode();
        DrainPendingActivityStarts();
    }

    protected override void OnPause()
    {
        _isResumed = false;
        base.OnPause();
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);

        if (hasFocus) ApplyPreferredDisplayMode();
    }

    public override bool DispatchTouchEvent(MotionEvent? ev)
    {
        PublishTouchEvent(ev);
        return base.DispatchTouchEvent(ev);
    }

    protected override void OnDestroy()
    {
        if (ReferenceEquals(Current, this))
        {
            Current = null;
            AndroidPlatformBridge.Instance.DetachActivity();
        }

        base.OnDestroy();
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        TaskCompletionSource<AndroidActivityResult>? completionSource;
        lock (RequestSync)
        {
            PendingResults.Remove(requestCode, out completionSource);
        }

        Log.Debug(
            LogTag,
            $"Activity result received. requestCode={requestCode}, result={resultCode}, matchedPending={completionSource is not null}, hasData={data is not null}.");
        completionSource?.TrySetResult(new AndroidActivityResult(resultCode, data));
    }

    private void ApplyPreferredDisplayMode()
    {
        if (Window is null) return;

        if (OperatingSystem.IsAndroidVersionAtLeast(35)) Window.FrameRatePowerSavingsBalanced = false;

        var display = Display;
        var attributes = Window.Attributes;
        if (attributes is null) return;

        var preferredMode = GetHighestRefreshMode(display);
        if (preferredMode is not null)
        {
            attributes.PreferredDisplayModeId = preferredMode.ModeId;
            attributes.PreferredRefreshRate = preferredMode.RefreshRate;
        }

        Window.Attributes = attributes;
    }

    private void PublishTouchEvent(MotionEvent? ev)
    {
        if (ev is null) return;

        if (ev.ActionMasked == MotionEventActions.Move)
        {
            var now = System.Environment.TickCount64;
            if (now - _lastPublishedMoveAtMilliseconds < 32) return;

            _lastPublishedMoveAtMilliseconds = now;
        }

        var scaling = Content is Control mainView
            ? TopLevel.GetTopLevel(mainView)?.RenderScaling ?? 1
            : 1;

        var rootPosition = new AvaloniaPoint(ev.GetX() / scaling, ev.GetY() / scaling);
        switch (ev.ActionMasked)
        {
            case MotionEventActions.Down:
            case MotionEventActions.Move:
            case MotionEventActions.PointerDown:
                WatcherPointerTracker.Move(rootPosition);
                break;
            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
            case MotionEventActions.PointerUp:
                WatcherPointerTracker.Release(rootPosition);
                break;
        }
    }

    private static Display.Mode? GetHighestRefreshMode(Display display)
    {
        return (display.GetSupportedModes() ?? [])
            .OrderByDescending(mode => mode.RefreshRate)
            .ThenByDescending(mode => (long)mode.PhysicalWidth * mode.PhysicalHeight)
            .FirstOrDefault();
    }

    private static void ApplyStartupMitigations()
    {
        if (_startupMitigationsApplied) return;

        _startupMitigationsApplied = true;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

        try
        {
            JavaSystem.LoadLibrary("android");
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Warn(LogTag, $"libandroid preload failed: {exception.Message}");
        }
    }

    private Task<AndroidActivityResult> StartForResultAsync(
        Intent intent,
        CancellationToken cancellationToken = default)
    {
        var completionSource = new TaskCompletionSource<AndroidActivityResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration cancellationRegistration = default;

        int requestCode;
        lock (RequestSync)
        {
            requestCode = _nextRequestCode++;
            PendingResults[requestCode] = completionSource;
        }

        Log.Debug(
            LogTag,
            $"Activity result request registered. requestCode={requestCode}, action={intent.Action ?? "<none>"}, isResumed={_isResumed}.");

        if (cancellationToken.CanBeCanceled)
            cancellationRegistration = cancellationToken.Register(() =>
            {
                lock (RequestSync)
                {
                    PendingResults.Remove(requestCode);
                }

                Log.Debug(
                    LogTag,
                    $"Activity result request canceled. requestCode={requestCode}, action={intent.Action ?? "<none>"}.");
                completionSource.TrySetCanceled(cancellationToken);
            });

        _ = completionSource.Task.ContinueWith(
            static (_, state) => ((CancellationTokenRegistration)state!).Dispose(),
            cancellationRegistration,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        StartActivityForResultOnUiThread(intent, requestCode, completionSource);

        return completionSource.Task;
    }

    private void StartActivityForResultOnUiThread(
        Intent intent,
        int requestCode,
        TaskCompletionSource<AndroidActivityResult> completionSource)
    {
        var request = new ActivityStartRequest(intent, requestCode, completionSource);

        void ScheduleStart()
        {
            if (!_isResumed)
            {
                QueueActivityStart(request);
                return;
            }

            StartActivityForResultRequest(request);
        }

        if (Looper.MainLooper?.IsCurrentThread == true)
        {
            ScheduleStart();
            return;
        }

        RunOnUiThread(ScheduleStart);
    }

    private void DrainPendingActivityStarts()
    {
        _pendingDrainScheduled = false;
        var drained = 0;
        while (_isResumed && drained < MaxActivityStartsPerDrain)
        {
            ActivityStartRequest request;
            lock (RequestSync)
            {
                if (PendingActivityStarts.Count == 0) return;

                Log.Debug(LogTag, $"Draining queued activity start. remainingBefore={PendingActivityStarts.Count}.");
                request = PendingActivityStarts.Dequeue();
            }

            StartActivityForResultRequest(request);
            drained++;
        }

        if (_isResumed && HasPendingActivityStarts()) SchedulePendingActivityDrain();
    }

    private void QueueActivityStart(ActivityStartRequest request)
    {
        lock (RequestSync)
        {
            if (!PendingResults.ContainsKey(request.RequestCode)) return;

            if (IsIconCommand(request.Intent)) DropQueuedIconRequestsLocked("coalesced_icon_request");

            while (PendingActivityStarts.Count >= MaxPendingActivityStarts)
                DropOldestPendingActivityStartLocked("pending_start_queue_limit");

            Log.Debug(
                LogTag,
                $"Queueing activity start until resume. requestCode={request.RequestCode}, action={request.Intent.Action ?? "<none>"}, pendingStarts={PendingActivityStarts.Count + 1}.");
            PendingActivityStarts.Enqueue(request);
        }

        if (_isResumed) SchedulePendingActivityDrain();
    }

    private void StartActivityForResultRequest(ActivityStartRequest request)
    {
        lock (RequestSync)
        {
            if (!PendingResults.ContainsKey(request.RequestCode)) return;
        }

        try
        {
            Log.Debug(
                LogTag,
                $"Starting activity for result. requestCode={request.RequestCode}, action={request.Intent.Action ?? "<none>"}.");
            StartActivityForResult(request.Intent, request.RequestCode);
        }
        catch (Exception exception)
        {
            lock (RequestSync)
            {
                PendingResults.Remove(request.RequestCode);
            }

            Log.Warn(LogTag, $"Failed to start activity for result: {exception}");
            request.CompletionSource.TrySetResult(
                AndroidActivityResultApi.CreateCanceledResult("Android не смог открыть нужный экран или действие."));
        }
    }

    private bool HasPendingActivityStarts()
    {
        lock (RequestSync)
        {
            return PendingActivityStarts.Count > 0;
        }
    }

    private void SchedulePendingActivityDrain()
    {
        if (_pendingDrainScheduled) return;

        _pendingDrainScheduled = true;
        var handler = new Handler(Looper.MainLooper ??
                                  throw new InvalidOperationException("Android main looper is unavailable."));
        handler.PostDelayed(DrainPendingActivityStarts, PendingDrainDelayMilliseconds);
    }

    private static void DropQueuedIconRequestsLocked(string reason)
    {
        if (PendingActivityStarts.Count == 0) return;

        var retained = new Queue<ActivityStartRequest>();
        while (PendingActivityStarts.Count > 0)
        {
            var queued = PendingActivityStarts.Dequeue();
            if (IsIconCommand(queued.Intent))
            {
                CancelQueuedActivityStartLocked(queued, reason);
                continue;
            }

            retained.Enqueue(queued);
        }

        while (retained.Count > 0) PendingActivityStarts.Enqueue(retained.Dequeue());
    }

    private static void DropOldestPendingActivityStartLocked(string reason)
    {
        if (PendingActivityStarts.Count == 0) return;

        var request = PendingActivityStarts.Dequeue();
        CancelQueuedActivityStartLocked(request, reason);
    }

    private static void CancelQueuedActivityStartLocked(ActivityStartRequest request, string reason)
    {
        PendingResults.Remove(request.RequestCode);
        Log.Debug(
            LogTag,
            $"Dropping queued activity start. requestCode={request.RequestCode}, action={request.Intent.Action ?? "<none>"}, reason={reason}.");
        request.CompletionSource.TrySetResult(
            AndroidActivityResultApi.CreateCanceledResult("Android отменил устаревшую фоновую команду."));
    }

    private static bool IsIconCommand(Intent intent)
    {
        return string.Equals(intent.Action, AgnosiaActions.QueryAppIcon, StringComparison.Ordinal)
               || string.Equals(intent.Action, AgnosiaActions.QueryAppIcons, StringComparison.Ordinal);
    }

    Activity IAndroidActivityHost.CurrentActivity => this;

    Type IAndroidActivityHost.CommandActivityType => typeof(DummyActivity);

    Type IAndroidActivityHost.AdminReceiverType => typeof(AgnosiaDeviceAdminReceiver);

    Type IAndroidActivityHost.WorkAppFrozenReceiverType => typeof(WorkAppFrozenReceiver);

    Task<AndroidActivityResult> IAndroidActivityHost.StartForResultAsync(Intent intent,
        CancellationToken cancellationToken)
    {
        return StartForResultAsync(intent, cancellationToken);
    }

    private sealed record ActivityStartRequest(
        Intent Intent,
        int RequestCode,
        TaskCompletionSource<AndroidActivityResult> CompletionSource);
}
using System.Globalization;
using Agnosia.Android.Activities;
using Agnosia.Android.Api;
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
using Log = Agnosia.Android.Api.AgnosiaLog;

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
    private static readonly Lock RequestSync = new();
    private static readonly Dictionary<int, TaskCompletionSource<AndroidActivityResult>> PendingResults = [];
    private static int _nextRequestCode = 4100;
    private static bool _startupMitigationsApplied;
    private readonly Queue<Action> _pendingActivityStarts = [];
    private bool _isResumed;

    private static MainActivity? Current { get; set; }

    static MainActivity()
    {
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            global::Android.Util.Log.Error(LogTag, $"Unhandled exception: {args.ExceptionObject}");
        };

        ApplyStartupMitigations();
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        ApplyStartupMitigations();

        AgnosiaRuntime.Initialize(this);
        ServiceRegistry.PlatformBridge = AndroidPlatformBridge.Instance;
        ServiceRegistry.InitialTheme = AndroidSettingsStore.LoadAppTheme(LocalStorageManager.Instance);
        AndroidPlatformBridge.Instance.AttachActivity(this);
        Current = this;

        base.OnCreate(savedInstanceState);

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(100);

                RunOnUiThread(() => ApplyPreferredDisplayMode());
                WorkProfileLockFreezeService.EnsureRunning(this);

                if (!AgnosiaUtilities.IsProfileOwner(this))
                {
                    return;
                }

                AgnosiaUtilities.EnforceWorkProfilePolicies(this, typeof(AgnosiaDeviceAdminReceiver), typeof(MainActivity));
                AgnosiaUtilities.EnforceUserRestrictions(this, typeof(AgnosiaDeviceAdminReceiver));

                await Task.Delay(50);
                RunOnUiThread(Finish);
            }
            catch (Exception exception)
            {
                Log.Error(LogTag, $"Background initialization failed: {exception}");
            }
        });
    }

    protected override void OnResume()
    {
        base.OnResume();
        _isResumed = true;
        Current = this;
        AndroidPlatformBridge.Instance.AttachActivity(this);
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

        if (hasFocus)
        {
            ApplyPreferredDisplayMode();
        }
    }

    public override bool DispatchTouchEvent(MotionEvent? ev)
    {
        PublishTouchEvent(ev, Content);
        return base.DispatchTouchEvent(ev);
    }

    protected override void OnDestroy()
    {
        if (ReferenceEquals(Current, this))
        {
            Current = null;
            AndroidPlatformBridge.Instance.DetachActivity();
            _pendingActivityStarts.Clear();
            CancelPendingActivityResults();
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

        completionSource?.TrySetResult(new AndroidActivityResult(resultCode, data));
    }

    private void ApplyPreferredDisplayMode()
    {
        if (Window is null)
        {
            return;
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(35))
        {
            Window.FrameRatePowerSavingsBalanced = false;
        }

        var display = Display;
        var attributes = Window.Attributes;
        if (attributes is null)
        {
            return;
        }

        var preferredMode = GetHighestRefreshMode(display);
        if (preferredMode is not null)
        {
            attributes.PreferredDisplayModeId = preferredMode.ModeId;
            attributes.PreferredRefreshRate = preferredMode.RefreshRate;
        }

        Window.Attributes = attributes;
    }

    private static void PublishTouchEvent(MotionEvent? ev, object? content)
    {
        if (ev is null)
        {
            return;
        }

        var scaling = content is Control mainView
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
        if (_startupMitigationsApplied)
        {
            return;
        }

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

        if (cancellationToken.CanBeCanceled)
        {
            cancellationRegistration = cancellationToken.Register(() =>
            {
                lock (RequestSync)
                {
                    PendingResults.Remove(requestCode);
                }

                completionSource.TrySetCanceled(cancellationToken);
            });
        }

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
        void Start()
        {
            lock (RequestSync)
            {
                if (!PendingResults.ContainsKey(requestCode))
                {
                    return;
                }
            }

            try
            {
                StartActivityForResult(intent, requestCode);
            }
            catch (Exception exception)
            {
                lock (RequestSync)
                {
                    PendingResults.Remove(requestCode);
                }

                Log.Warn(LogTag, $"Failed to start activity for result: {exception}");
                completionSource.TrySetResult(
                    AndroidActivityResultApi.CreateCanceledResult("Android не смог открыть нужный экран или действие."));
            }
        }

        void ScheduleStart()
        {
            if (!_isResumed)
            {
                _pendingActivityStarts.Enqueue(Start);
                return;
            }

            Start();
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
        while (_isResumed && _pendingActivityStarts.Count > 0)
        {
            _pendingActivityStarts.Dequeue().Invoke();
        }
    }

    private static void CancelPendingActivityResults()
    {
        TaskCompletionSource<AndroidActivityResult>[] pendingResults;
        lock (RequestSync)
        {
            pendingResults = PendingResults.Values.ToArray();
            PendingResults.Clear();
        }

        foreach (var completionSource in pendingResults)
        {
            completionSource.TrySetResult(
                AndroidActivityResultApi.CreateCanceledResult("Экран Agnosia был закрыт до завершения системного действия."));
        }
    }

    Activity IAndroidActivityHost.CurrentActivity => this;

    Type IAndroidActivityHost.CommandActivityType => typeof(DummyActivity);

    Type IAndroidActivityHost.AdminReceiverType => typeof(AgnosiaDeviceAdminReceiver);

    Type IAndroidActivityHost.WorkAppFrozenReceiverType => typeof(WorkAppFrozenReceiver);

    Task<AndroidActivityResult> IAndroidActivityHost.StartForResultAsync(Intent intent, CancellationToken cancellationToken) =>
        StartForResultAsync(intent, cancellationToken);
}

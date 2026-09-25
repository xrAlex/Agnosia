using System.Globalization;
using Agnosia.Android.Activities;
using Agnosia.Android.Infrastructure;
using Agnosia.Android.Receivers;
using Agnosia.Models;
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
    Name = "com.agnosia.app.MainActivity",
    Label = "@string/app_name",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@mipmap/ic_launcher",
    Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public partial class MainActivity : AvaloniaMainActivity, IAndroidActivityHost
{
    public const string LauncherActivityName = "com.agnosia.app.LauncherActivity";
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
    private CommandProfileAvailabilityReceiver? _commandProfileReceiver;
    private bool _pendingDrainScheduled;
    private readonly ActivityResumeRefreshPolicy _activityResumeRefreshPolicy = new();
    private long _lastPublishedMoveAtMilliseconds;

    private static MainActivity? Current { get; set; }

    internal static bool CanPrepareCommandAccess
    {
        get
        {
            lock (RequestSync)
                return Current is { _isResumed: true, IsFinishing: false }
                       && PendingResults.Count == 0 && PendingActivityStarts.Count == 0;
        }
    }

    static MainActivity()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            global::Android.Util.Log.Error(LogTag, $"Unhandled exception: {args.ExceptionObject}");
        };
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        if (IsProfileOwnerStartup())
        {
            AndroidStartup.SuppressPrimaryUiStartup();
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
        AndroidStartup.ConfigurePrimaryProfileServices(this);
        ServiceRegistry.GetRequiredService<AndroidPlatformBridge>().AttachActivity(this);
        Current = this;
        // Keep availability notifications active when the user changes transport without restarting.
        _commandProfileReceiver = CommandProfileAvailabilityReceiver.Register(this);
    }

    private void StartBackgroundInitialization()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(100).ConfigureAwait(false);

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
        return AndroidStartup.TryIsProfileOwner(this, LogTag, "Profile-owner startup check failed");
    }

    private void BootstrapWorkProfileAndFinish()
    {
        try
        {
            AgnosiaRuntime.Initialize(this);
            AndroidStartup.EnforceWorkProfilePoliciesAndStartLockFreezeMonitor(this, true);
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
        UiAnimationState.SetActive(true);
        Current = this;
        ServiceRegistry.GetRequiredService<AndroidPlatformBridge>().AttachActivity(this);
        if (ProviderTransportOptions.Enabled) _ = PrepareCommandAccessAsync();
        if (_activityResumeRefreshPolicy.ShouldRefreshDashboardOnResume())
            ServiceRegistry.NotifyPrimaryActivityResumed();
        DrainPendingActivityStarts();
        _ = RecoverVpnOnResumeAsync();
    }

    protected override void OnStart()
    {
        base.OnStart();
        if (ProviderTransportOptions.Enabled)
            ServiceRegistry.GetRequiredService<CommandAccessCoordinator>().NotifyForegroundSession();
    }

    private async Task PrepareCommandAccessAsync()
    {
        try
        {
            var access = ServiceRegistry.GetRequiredService<CommandAccessCoordinator>();
            if (!access.IsConnecting)
                await access.PrepareInForegroundAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) { Log.Warn(LogTag, $"Provider preparation failed: {exception.GetType().Name}."); }
    }

    internal static void PrepareCommandAccessForPreferenceChange()
    {
        if (!CanPrepareCommandAccess) return;
        MainActivity? activity;
        lock (RequestSync) activity = Current;
        if (activity is not null) _ = activity.PrepareCommandAccessAsync();
    }

    protected override void OnPause()
    {
        _isResumed = false;
        UiAnimationState.SetActive(false);
        base.OnPause();
    }

    public override bool DispatchTouchEvent(MotionEvent? ev)
    {
        PublishTouchEvent(ev);
        return base.DispatchTouchEvent(ev);
    }

    protected override void OnDestroy()
    {
        if (_commandProfileReceiver is not null)
        {
            UnregisterReceiver(_commandProfileReceiver);
            _commandProfileReceiver.Dispose();
            _commandProfileReceiver = null;
        }
        if (ReferenceEquals(Current, this))
        {
            Current = null;
            ServiceRegistry.GetRequiredService<AndroidPlatformBridge>().DetachActivity();
        }

        base.OnDestroy();
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

    Activity IAndroidActivityHost.CurrentActivity => this;

    Type IAndroidActivityHost.CommandActivityType => typeof(DummyActivity);

    Type IAndroidActivityHost.AdminReceiverType => typeof(AgnosiaDeviceAdminReceiver);

    Type IAndroidActivityHost.WorkAppFrozenReceiverType => typeof(WorkAppFrozenReceiver);

    Task<AndroidActivityResult> IAndroidActivityHost.StartForResultAsync(Intent intent,
        CancellationToken cancellationToken, Action? beforeStart)
    {
        return StartForResultAsync(intent, cancellationToken, beforeStart: beforeStart);
    }

    Task<OperationResult> IAndroidActivityHost.DisconnectPreparedVpnAsync(CancellationToken cancellationToken)
    {
        return TransientVpnDisconnectService.DisconnectPreparedVpnAsync(this, cancellationToken);
    }

    void IAndroidActivityHost.ShowVpnGuardOverlay()
    {
        OverlayVpnService.ShowOverlay(this);
    }

    void IAndroidActivityHost.HideVpnGuardOverlay()
    {
        OverlayVpnService.HideOverlay(this);
    }

}

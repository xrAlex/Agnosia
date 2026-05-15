using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Provider;
using Android.Views;
using Exception = Java.Lang.Exception;
using Extensions = Android.Runtime.Extensions;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Services;

[Service(Exported = false)]
public sealed class OverlayVpnService : Service
{
    private const string LogTag = "AgnosiaOverlayVpn";
    private const string ActionShowOverlay = "agnosia.action.SHOW_VPN_OVERLAY";
    private const string ActionHideOverlay = "agnosia.action.HIDE_VPN_OVERLAY";

    private const int OverlaySizeDp = 24;
    private const int OverlayMarginDp = 8;
    private const int OverlayAlpha = 60;

    private static readonly Color OverlayColor = Color.Argb(OverlayAlpha, 100, 150, 255);
    private static readonly object HideConnectionsGate = new();
    private static readonly HashSet<HideOverlayServiceConnection> HideConnections = [];

    private IWindowManager? _windowManager;
    private View? _overlayView;
    private WindowManagerLayoutParams? _layoutParams;
    private readonly OverlayBinder _binder;

    public OverlayVpnService()
    {
        _binder = new OverlayBinder(this);
    }

    public static void HideOverlay(Context context)
    {
        var intent = new Intent(context, typeof(OverlayVpnService));
        var appContext = context.ApplicationContext ?? context;
        var connection = new HideOverlayServiceConnection(appContext);

        lock (HideConnectionsGate)
        {
            HideConnections.Add(connection);
        }

        try
        {
            if (appContext.BindService(intent, connection, default(Bind))) return;

            ReleaseHideConnection(connection);
            Log.Debug(LogTag, "Overlay service is not running; no overlay hide command is needed.");
        }
        catch (Exception exception)
        {
            ReleaseHideConnection(connection);
            Log.Warn(LogTag, $"Android не смог привязаться к overlay service для скрытия overlay. Details: {exception}");
        }
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var action = intent?.Action;
        if (string.Equals(action, ActionShowOverlay, StringComparison.Ordinal))
        {
            ShowOverlayWindow();
        }
        else if (string.Equals(action, ActionHideOverlay, StringComparison.Ordinal))
        {
            HideOverlayWindow();
            StopSelf(startId);
        }

        if (!string.Equals(action, ActionShowOverlay, StringComparison.Ordinal)
            && !string.Equals(action, ActionHideOverlay, StringComparison.Ordinal))
            StopSelf(startId);

        return StartCommandResult.NotSticky;
    }

    public override void OnDestroy()
    {
        HideOverlayWindow();
        base.OnDestroy();
    }

    public override IBinder? OnBind(Intent? intent)
    {
        return _binder;
    }

    private void ShowOverlayWindow()
    {
        if (_overlayView is not null)
        {
            Log.Debug(LogTag, "Overlay window is already visible; skipping duplicate show.");
            return;
        }

        var wmService = GetSystemService(WindowService);
        if (wmService is null)
        {
            Log.Warn(LogTag, "WindowService is unavailable; cannot show overlay.");
            return;
        }

        _windowManager = Extensions.JavaCast<IWindowManager>(wmService);
        if (_windowManager is null)
        {
            Log.Warn(LogTag, "IWindowManager is unavailable; cannot show overlay.");
            return;
        }

        if (!Settings.CanDrawOverlays(this))
        {
            Log.Warn(LogTag, "SYSTEM_ALERT_WINDOW permission not granted; cannot show overlay.");
            return;
        }

        var density = Resources?.DisplayMetrics?.Density ?? 1f;
        var sizeInPx = (int)(OverlaySizeDp * density + 0.5f);
        var marginInPx = (int)(OverlayMarginDp * density + 0.5f);

        _overlayView = new View(this);
        _overlayView.SetBackgroundColor(OverlayColor);

        _layoutParams = new WindowManagerLayoutParams(
            sizeInPx,
            sizeInPx,
            WindowManagerTypes.ApplicationOverlay,
            WindowManagerFlags.NotFocusable
            | WindowManagerFlags.NotTouchable
            | WindowManagerFlags.LayoutInScreen
            | WindowManagerFlags.LayoutNoLimits,
            Format.Translucent);

        _layoutParams.Gravity = GravityFlags.Top | GravityFlags.Right;
        _layoutParams.X = marginInPx;
        _layoutParams.Y = marginInPx;

        try
        {
            _windowManager!.AddView(_overlayView!, _layoutParams!);
            Log.Info(
                LogTag,
                $"Overlay window shown. size={OverlaySizeDp}dp x {OverlaySizeDp}dp, " +
                $"margin={OverlayMarginDp}dp, alpha={OverlayAlpha}, gravity=TOP|RIGHT.");
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Failed to show overlay window: {exception.Message}");
            CleanupOverlay();
        }
    }

    private void HideOverlayWindow()
    {
        if (_windowManager is null || _overlayView is null) return;

        try
        {
            _windowManager.RemoveView(_overlayView);
            Log.Info(LogTag, "Overlay window hidden.");
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Failed to hide overlay window: {exception.Message}");
        }

        CleanupOverlay();
    }

    private void CleanupOverlay()
    {
        _overlayView = null;
        _layoutParams = null;
        _windowManager = null;
    }

    private static void ReleaseHideConnection(HideOverlayServiceConnection connection)
    {
        lock (HideConnectionsGate)
        {
            HideConnections.Remove(connection);
        }
    }

    private sealed class OverlayBinder(OverlayVpnService service) : Binder
    {
        public OverlayVpnService Service { get; } = service;
    }

    private sealed class HideOverlayServiceConnection(Context context) : Java.Lang.Object, IServiceConnection
    {
        public void OnServiceConnected(ComponentName? name, IBinder? service)
        {
            try
            {
                if (service is OverlayBinder binder)
                {
                    binder.Service.HideOverlayWindow();
                    binder.Service.StopSelf();
                }
                else
                {
                    Log.Warn(LogTag, "Overlay service returned an unexpected binder while hiding overlay.");
                }
            }
            finally
            {
                UnbindAndRelease();
            }
        }

        public void OnServiceDisconnected(ComponentName? name)
        {
            ReleaseHideConnection(this);
        }

        private void UnbindAndRelease()
        {
            try
            {
                context.UnbindService(this);
            }
            catch (Exception exception)
            {
                Log.Warn(LogTag, $"Android не смог отвязаться от overlay service после скрытия overlay. Details: {exception}");
            }
            finally
            {
                ReleaseHideConnection(this);
            }
        }
    }
}

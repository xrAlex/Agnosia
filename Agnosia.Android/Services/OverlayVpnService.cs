using Agnosia.Android.Api.Platform;
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

    private IWindowManager? _windowManager;
    private View? _overlayView;
    private WindowManagerLayoutParams? _layoutParams;

    public static void HideOverlay(Context context)
    {
        var intent = new Intent(context, typeof(OverlayVpnService));
        intent.SetAction(ActionHideOverlay);
        AndroidServiceApi.TryStartService(
            context,
            intent,
            LogTag,
            "Android не смог отправить команду скрытия overlay.");
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
        return null;
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
}
using Agnosia.Android.Api.Platform;
using Android.App;
using Android.App.Job;
using Android.Content;
using Android.OS;
using JavaClass = Java.Lang.Class;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Services;

[Service(
    Name = "com.agnosia.app.LockFreezeCleanupJobService",
    Permission = "android.permission.BIND_JOB_SERVICE",
    Exported = false)]
public sealed class LockFreezeCleanupJobService : JobService
{
    private const string LogTag = "AgnosiaLockFreezeJob";
    private const string ExtraTrigger = "trigger";
    private const int JobId = 0x57C33;

    public static string Schedule(Context context, string trigger)
    {
        try
        {
            AgnosiaRuntime.Initialize(context);
            if (!AgnosiaUtilities.IsProfileOwner(context)) return "skipped_not_profile_owner";
            if (!HiddenAppSessionMonitorService.HasPersistedSessionForScreenLock())
            {
                Log.Info(LogTag, $"Lock-freeze cleanup job skipped_no_session. trigger={trigger}.");
                return "skipped_no_session";
            }

            if (context.GetSystemService(Context.JobSchedulerService) is not JobScheduler scheduler)
            {
                Log.Warn(LogTag, $"Lock-freeze cleanup job failed: JobScheduler unavailable. trigger={trigger}.");
                return "failed";
            }

            var extras = new PersistableBundle();
            extras.PutString(ExtraTrigger, trigger);
            var serviceClass = JavaClass.FromType(typeof(LockFreezeCleanupJobService))
                               ?? throw new InvalidOperationException("Lock-freeze cleanup job class unavailable.");
            var component = new ComponentName(context, serviceClass);
            var builder = new JobInfo.Builder(JobId, component);
            builder.SetExtras(extras);
            builder.SetOverrideDeadline(0);
            var job = builder.Build()
                      ?? throw new InvalidOperationException("Lock-freeze cleanup job could not be built.");

            var result = scheduler.Schedule(job);
            if (result == JobScheduler.ResultSuccess)
            {
                Log.Info(LogTag, $"Lock-freeze cleanup job scheduled. trigger={trigger}.");
                return "scheduled";
            }

            Log.Warn(LogTag, $"Lock-freeze cleanup job failed. trigger={trigger}; scheduleResult={result}.");
            return "failed";
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Lock-freeze cleanup job failed. trigger={trigger}; error={exception}");
            return "failed";
        }
    }

    public static void RunStartupSafetyNet(Context context)
    {
        try
        {
            AgnosiaRuntime.Initialize(context);
            var result = TryCompletePersistedSession(context, "android_startup");
            Log.Info(LogTag, $"Lock-freeze startup safety net result={result}.");
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Lock-freeze startup safety net failed: {exception}");
        }
    }

    public override bool OnStartJob(JobParameters? parameters)
    {
        var trigger = parameters?.Extras?.GetString(ExtraTrigger) ?? "job";
        _ = Task.Run(() =>
        {
            try
            {
                AgnosiaRuntime.Initialize(this);
                var result = TryCompletePersistedSession(this, trigger);
                Log.Info(LogTag, $"Lock-freeze cleanup job completed. trigger={trigger}; result={result}.");
            }
            finally
            {
                if (parameters is not null) JobFinished(parameters, false);
            }
        });
        return true;
    }

    public override bool OnStopJob(JobParameters? parameters)
    {
        Log.Info(LogTag, "Lock-freeze cleanup job stopped by Android.");
        return false;
    }

    private static string TryCompletePersistedSession(Context context, string trigger)
    {
        if (!AgnosiaUtilities.IsProfileOwner(context)) return "skipped_not_profile_owner";
        if (!HiddenAppSessionMonitorService.HasPersistedSessionForScreenLock()) return "skipped_no_session";
        if (AndroidSystemApi.GetPowerManager(context)?.IsInteractive != false) return "skipped_interactive";

        return HiddenAppSessionMonitorService.CompletePersistedSessionForScreenLock(context)
            ? "completed"
            : "failed";
    }
}

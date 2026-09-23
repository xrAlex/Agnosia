using System.Text;
using ProcessBuilder = Java.Lang.ProcessBuilder;
using Exception = System.Exception;

namespace Agnosia.Android.Platform;

internal sealed class AndroidRootCommandRunner : IRootCommandRunner
{
    private static readonly TimeSpan SessionTimeout = TimeSpan.FromSeconds(90);
    private const string ReadyMarker = "AGNOSIA_ROOT_READY";
    // timeout owns the actual native command PID inside the privileged session, including with daemon-based su.
    private const string DeadlineShell = "exec /system/bin/timeout -s KILL 75 /system/bin/sh";
    private const int MaximumOutputCharacters = 1024 * 1024;

    public Task<RootCommandResult> RunAsync(string command, CancellationToken cancellationToken) =>
        Task.Run(() => RunCoreAsync(command, cancellationToken), cancellationToken);

    private static async Task<RootCommandResult> RunCoreAsync(string command, CancellationToken cancellationToken)
    {
        Java.Lang.Process? process = null;
        StreamReader? stdout = null;
        StreamReader? stderr = null;
        StreamWriter? stdin = null;
        Task<string>? outputTask = null;
        Task<string>? errorTask = null;
        Task<int>? exitTask = null;
        Task<string?>? readyTask = null;
        var submitted = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var builder = new ProcessBuilder("su", "-c", DeadlineShell);
            process = builder.Start() ?? throw new IOException("Could not start su.");
            stdout = new StreamReader(process.InputStream!);
            stderr = new StreamReader(process.ErrorStream!);
            stdin = new StreamWriter(process.OutputStream!, new UTF8Encoding(false));
            exitTask = Task.Run(process.WaitFor, CancellationToken.None);
            errorTask = ReadBoundedAsync(stderr, CancellationToken.None);

            // Wait until su has granted access and the privileged deadline is running. No mutation is queued
            // during the permission prompt; a delayed grant after cancellation can only execute this marker.
            using (var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                startup.CancelAfter(SessionTimeout);
                await stdin.WriteAsync(("echo " + ReadyMarker + "\n").AsMemory(), startup.Token).ConfigureAwait(false);
                await stdin.FlushAsync(startup.Token).ConfigureAwait(false);
                readyTask = stdout.ReadLineAsync(startup.Token).AsTask();
                var ready = await readyTask.WaitAsync(startup.Token).ConfigureAwait(false);
                if (ready != ReadyMarker) throw new IOException("Privileged deadline shell was not started.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            using var deadline = new CancellationTokenSource(SessionTimeout);
            outputTask = ReadBoundedAsync(stdout, CancellationToken.None);
            // Once submitted, hold the coordinator's lock until the privileged process and both streams finish.
            // Cancellation stops subsequent commands; it must not release the lock while a root mutation runs.
            submitted = true;
            await stdin.WriteAsync(("exec " + DirectProfileProvisioningCommands.AsNativeExecutable(command) + "\n").AsMemory(),
                deadline.Token).ConfigureAwait(false);
            await stdin.FlushAsync(deadline.Token).ConfigureAwait(false);
            stdin.Close();
            await Task.WhenAll(outputTask, errorTask, exitTask).WaitAsync(deadline.Token).ConfigureAwait(false);
            var exitCode = await exitTask.ConfigureAwait(false);
            if (exitCode is 124 or 137) throw new RootCommandOutcomeUnknownException();
            return new RootCommandResult(exitCode, await outputTask.ConfigureAwait(false), await errorTask.ConfigureAwait(false));
        }
        catch (Exception exception) when (submitted && exception is OperationCanceledException or IOException or Java.Lang.Throwable)
        {
            throw new RootCommandOutcomeUnknownException();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Waiting for root access timed out.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is Java.Lang.Throwable or IOException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Do not include the script, stdout, stderr, or exception text in diagnostics.
            throw new IOException("Root command could not be executed.");
        }
        finally
        {
            if (process is not null)
            {
                TryDestroy(process);
            }
            TryClose(stdin);
            TryClose(stdout);
            TryClose(stderr);
            // These tasks can outlive a broken native pipe. Observe every late failure during cleanup.
            Observe(outputTask);
            Observe(errorTask);
            Observe(exitTask);
            Observe(readyTask);
            if (exitTask is { IsCompleted: false })
                _ = exitTask.ContinueWith(_ => process?.Dispose(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            else process?.Dispose();
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var result = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (result.Length + count > MaximumOutputCharacters) throw new IOException("Root output exceeded its limit.");
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }

    private static void TryDestroy(Java.Lang.Process process)
    {
        try { process.Destroy(); }
        catch (Exception) { /* Best effort when su already exited. */ }
    }

    private static void TryClose(IDisposable? stream)
    {
        try { stream?.Dispose(); }
        catch (Exception) { /* Cleanup after a broken pipe must not hide the uncertain command outcome. */ }
    }

    private static void Observe(Task? task)
    {
        if (task is null) return;
        _ = task.ContinueWith(completed => { _ = completed.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}

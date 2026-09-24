namespace Agnosia.Android.Commands;

internal sealed class CommandDispatchState
{
    private int _mayHaveDispatched;
    public bool MayHaveDispatched => Volatile.Read(ref _mayHaveDispatched) != 0;

    public T Invoke<T>(Func<T> call, CancellationToken cancellationToken)
    {
        // A waiter can observe cancellation at any instruction. Publish uncertainty before
        // the final cancellation check, so a subsequent native call can never look unsent.
        Volatile.Write(ref _mayHaveDispatched, 1);
        cancellationToken.ThrowIfCancellationRequested();
        return call();
    }
}

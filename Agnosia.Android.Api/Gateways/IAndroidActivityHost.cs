using Android.Content;

namespace Agnosia.Android.Api.Gateways;

public interface IAndroidActivityHost
{
    Activity CurrentActivity { get; }

    Type CommandActivityType { get; }

    Type AdminReceiverType { get; }

    Type WorkAppFrozenReceiverType { get; }

    Task<AndroidActivityResult> StartForResultAsync(Intent intent, CancellationToken cancellationToken = default);
}

public readonly record struct AndroidActivityResult(Result ResultCode, Intent? Data);
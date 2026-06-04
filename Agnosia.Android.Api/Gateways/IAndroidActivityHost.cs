using Agnosia.Models;
using Android.Content;

namespace Agnosia.Android.Api.Gateways;

public interface IAndroidActivityHost
{
    Activity CurrentActivity { get; }

    Type CommandActivityType { get; }

    Type AdminReceiverType { get; }

    Type WorkAppFrozenReceiverType { get; }

    Task<AndroidActivityResult> StartForResultAsync(Intent intent, CancellationToken cancellationToken = default);

    Task<OperationResult> DisconnectPreparedVpnAsync(CancellationToken cancellationToken = default);

    void ShowVpnGuardOverlay();

    void HideVpnGuardOverlay();
}

public readonly record struct AndroidActivityResult(Result ResultCode, Intent? Data);

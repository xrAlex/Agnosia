using Agnosia.Android.Api.Commands;
using Agnosia.Models;
using Android.Content;

namespace Agnosia.Android.Gateways;

public static class AndroidActivityResultApi
{
    public static string? ExtractMessage(AndroidActivityResult result)
    {
        return result.Data?.GetStringExtra(AndroidCommandContract.ResultMessage);
    }

    public static string? ExtractError(AndroidActivityResult result)
    {
        return result.Data?.GetStringExtra(AndroidCommandContract.ResultError);
    }

    private static AndroidAppLaunchResult? ExtractLaunchResult(AndroidActivityResult result)
    {
        return AndroidAppLaunchResult.TryRead(result.Data, out var launchResult)
            ? launchResult
            : null;
    }

    public static AndroidActivityResult CreateCanceledResult(string error)
    {
        var data = new Intent();
        data.PutExtra(AndroidCommandContract.ResultError, error);
        return new AndroidActivityResult(Result.Canceled, data);
    }

    public static OperationResult ToPackageOperationResult(AndroidActivityResult result, string successMessage)
    {
        if (result.ResultCode == Result.Ok)
        {
            var message = ExtractMessage(result);
            return OperationResult.Success(string.IsNullOrWhiteSpace(message) ? successMessage : message);
        }

        var error = ExtractError(result);
        if (string.Equals(error, AndroidCommandContract.ErrorSystemAppUnsupported, StringComparison.Ordinal))
            return OperationResult.Failure(
                "Системные приложения можно включать или скрывать только внутри рабочего профиля.");

        return OperationResult.Failure(string.IsNullOrWhiteSpace(error)
            ? "Android не смог выполнить операцию с пакетом."
            : error);
    }

    public static OperationResult ToVoidOperationResult(AndroidActivityResult result, string successMessage)
    {
        if (ExtractLaunchResult(result) is { } launchResult) return launchResult.ToOperationResult();

        var message = ExtractMessage(result);
        var error = ExtractError(result);
        return result.ResultCode == Result.Canceled
            ? OperationResult.Failure(string.IsNullOrWhiteSpace(error) ? "Android отклонил запрос." : error)
            : OperationResult.Success(string.IsNullOrWhiteSpace(message) ? successMessage : message);
    }
}
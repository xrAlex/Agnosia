namespace Agnosia.Android.Commands;

internal static class ActivityCommandResultIdentity
{
    public static ActivityCommandResultIdentityValidation Validate(
        Guid expectedCorrelationId,
        AndroidCommandKind expectedKind,
        int expectedResultCode,
        string? actualCorrelationId,
        string? actualKind,
        int actualResultCode)
    {
        if (!Guid.TryParse(actualCorrelationId, out var correlationId)
            || correlationId != expectedCorrelationId)
            return ActivityCommandResultIdentityValidation.Failure(
                "activity_result_correlation_mismatch");

        if (!Enum.TryParse<AndroidCommandKind>(actualKind, out var kind)
            || kind != expectedKind)
            return ActivityCommandResultIdentityValidation.Failure(
                "activity_result_kind_mismatch");

        return actualResultCode == expectedResultCode
            ? ActivityCommandResultIdentityValidation.Success
            : ActivityCommandResultIdentityValidation.Failure(
                "activity_result_code_mismatch");
    }
}

internal sealed record ActivityCommandResultIdentityValidation(bool Succeeded, string? ErrorCode)
{
    public static ActivityCommandResultIdentityValidation Success { get; } = new(true, null);

    public static ActivityCommandResultIdentityValidation Failure(string errorCode)
    {
        return new ActivityCommandResultIdentityValidation(false, errorCode);
    }
}

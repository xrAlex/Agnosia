using Agnosia.Android.Commands;
using Agnosia.Android.Gateways;
using Xunit;

namespace Agnosia.Unit.Android.Gateways;

public sealed class WorkProfileOwnerCheckInterpreterTests
{
    [Fact]
    public void Interpret_accepts_complete_profile_owner_payload_from_command_transport()
    {
        var result = AndroidCommandResultEnvelope.Success(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            AndroidCommandKind.RecoverAuthentication,
            AndroidCommandTransportKind.SilentWorkProfile,
            """
            {
              "profile_owner_check_performed": true,
              "is_profile_owner": true,
              "app_version_code": 42,
              "app_version_name": "0.9"
            }
            """,
            "Recovered.",
            TimeSpan.FromMilliseconds(15),
            "actual=Work");

        var ownerCheck = WorkProfileOwnerCheckInterpreter.Interpret(result);

        Assert.Equal(WorkProfileOwnerCheckKind.AppIsProfileOwner, ownerCheck.Kind);
        Assert.Equal(42, ownerCheck.AppVersionCode);
        Assert.Equal("0.9", ownerCheck.AppVersionName);
        Assert.Contains("transport=SilentWorkProfile", ownerCheck.DiagnosticReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Interpret_preserves_failed_transport_diagnostics()
    {
        var result = AndroidCommandResultEnvelope.Failure(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            AndroidCommandKind.RecoverAuthentication,
            AndroidCommandTransportKind.SilentWorkProfile,
            "Unavailable.",
            "silent_work_transport_unavailable",
            TimeSpan.Zero,
            "canInteractAcrossProfiles=false");

        var ownerCheck = WorkProfileOwnerCheckInterpreter.Interpret(result);

        Assert.Equal(WorkProfileOwnerCheckKind.Unreachable, ownerCheck.Kind);
        Assert.Contains("profilePing=silent_work_transport_unavailable", ownerCheck.DiagnosticReason,
            StringComparison.Ordinal);
        Assert.Contains("canInteractAcrossProfiles=false", ownerCheck.DiagnosticReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Interpret_rejects_success_without_performed_owner_check()
    {
        var result = AndroidCommandResultEnvelope.Success(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            AndroidCommandKind.RecoverAuthentication,
            AndroidCommandTransportKind.SilentWorkProfile,
            "{\"is_profile_owner\":true}",
            "Recovered.",
            TimeSpan.Zero,
            string.Empty);

        var ownerCheck = WorkProfileOwnerCheckInterpreter.Interpret(result);

        Assert.Equal(WorkProfileOwnerCheckKind.Unreachable, ownerCheck.Kind);
        Assert.Contains("profilePing=payloadIncomplete", ownerCheck.DiagnosticReason, StringComparison.Ordinal);
    }
}

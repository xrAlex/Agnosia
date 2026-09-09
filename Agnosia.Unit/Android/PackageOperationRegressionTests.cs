using Agnosia.Android.Api.Platform;
using Agnosia.Android.Platform;
using Xunit;

namespace Agnosia.Unit.Android;

public sealed class PackageOperationRegressionTests
{
    [Fact]
    public void Icon_bytes_are_signed_by_content()
    {
        Assert.Equal("bytes:AQID", IntentSignatureValueEncoder.Encode(new byte[] { 1, 2, 3 }));
        Assert.NotEqual(IntentSignatureValueEncoder.Encode(new byte[] { 1 }),
            IntentSignatureValueEncoder.Encode(new byte[] { 2 }));
    }

    [Fact]
    public void Bundle_signature_survives_reordering_but_detects_changed_icon()
    {
        var original = new Dictionary<string, object?> { ["a"] = new byte[] { 1 }, ["b"] = true };
        var transported = new Dictionary<string, object?> { ["b"] = true, ["a"] = new byte[] { 1 } };
        Assert.Equal(IntentSignatureValueEncoder.Encode(original), IntentSignatureValueEncoder.Encode(transported));
        transported["a"] = new byte[] { 2 };
        Assert.NotEqual(IntentSignatureValueEncoder.Encode(original), IntentSignatureValueEncoder.Encode(transported));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Stale_android_policy_is_reapplied_when_actual_package_state_disagrees(bool requested)
    {
        var policy = requested;
        var actual = !requested;
        var writes = new List<bool>();
        var result = ApplicationHiddenPolicy.Apply(requested, () => actual, value =>
        {
            writes.Add(value);
            if (policy != value) actual = value; // Android 15 only enforces changed policy.
            policy = value;
            return actual == value;
        }, repairUnchangedPolicy: true);

        Assert.True(result);
        Assert.Equal(requested, actual);
        Assert.Equal(new[] { requested, !requested, requested }, writes);
    }

    [Fact]
    public void Rejected_hide_does_not_report_success()
    {
        Assert.False(ApplicationHiddenPolicy.Apply(true, () => false, _ => false, true));
    }

    [Fact]
    public void Confirmed_hidden_app_is_never_temporarily_unhidden()
    {
        var writes = new List<bool>();
        Assert.True(ApplicationHiddenPolicy.Apply(true, () => true, value => { writes.Add(value); return true; }, true));
        Assert.DoesNotContain(false, writes);
    }
}

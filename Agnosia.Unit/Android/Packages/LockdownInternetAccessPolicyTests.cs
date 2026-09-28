using Agnosia.Android.Packages;
using Xunit;

namespace Agnosia.Unit.Android.Packages;

public sealed class LockdownInternetAccessPolicyTests
{
    [Fact]
    public void Saved_selection_is_not_an_active_block_when_lockdown_is_off()
    {
        string[] saved = ["com.example.blocked"];

        Assert.Empty(LockdownInternetAccessPolicy.ResolveActiveBlockedPackages(false, saved));
        Assert.Contains("com.example.blocked", saved);
        Assert.Contains("com.example.blocked", LockdownInternetAccessPolicy.ResolveActiveBlockedPackages(true, saved));
    }
}

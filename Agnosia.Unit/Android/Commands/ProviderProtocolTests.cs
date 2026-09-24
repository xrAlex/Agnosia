using Agnosia.Android.Api.Commands;
using Agnosia.Android.Commands;
using Xunit;

namespace Agnosia.Unit.Android.Commands;

public sealed class ProviderProtocolTests
{
    private const string Key = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
    private static ProviderCommandMessage Request() => new(1, Guid.NewGuid(), "FreezePackage", 0, 10,
        "generation", 1000, 2000, false, false, "{\"package\":\"test.app\"}", null, "", "");

    [Fact]
    public void SignatureRejectsChangesToCommandProfileDeadlineAndResult()
    {
        var signed = ProviderCommandProtocol.Sign(Request(), Key);
        Assert.True(ProviderCommandProtocol.Verify(signed, Key));
        foreach (var changed in new[] { signed with { CommandKind = "UnfreezePackage" },
                     signed with { TargetUserId = 11 }, signed with { ProfileGeneration = "recreated" },
                     signed with { Deadline = 3000 }, signed with { IsResponse = true },
                     signed with { Succeeded = true }, signed with { PayloadJson = "{}" },
                     signed with { ErrorCode = "ok" } })
            Assert.False(ProviderCommandProtocol.Verify(changed, Key));
        Assert.False(ProviderCommandProtocol.Verify(signed with { Signature = "bad" }, Key));
    }

    [Fact]
    public void RequestValidationBindsActualUsersGenerationAndFreshness()
    {
        var request = Request();
        Assert.Null(ProviderCommandProtocol.ValidateRequest(request, 0, 10, "generation", 1500));
        Assert.Equal("wrong_profile", ProviderCommandProtocol.ValidateRequest(request, 1, 10, "generation", 1500));
        Assert.Equal("wrong_profile", ProviderCommandProtocol.ValidateRequest(request, 0, 10, "recreated", 1500));
        Assert.Equal("deadline_exceeded", ProviderCommandProtocol.ValidateRequest(request, 0, 10, "generation", 2100));
        Assert.Equal("invalid_request", ProviderCommandProtocol.ValidateRequest(request with { IsResponse = true }, 0, 10, "generation", 1500));
    }

    [Fact]
    public void ReplyFromAnotherCallOrProfileIsRejected()
    {
        var request = Request();
        var response = ProviderCommandProtocol.Reply(request, true, "{}", null, "done");
        Assert.True(ProviderCommandProtocol.MatchesReply(request, response));
        Assert.False(ProviderCommandProtocol.MatchesReply(request, response with { CorrelationId = Guid.NewGuid() }));
        Assert.False(ProviderCommandProtocol.MatchesReply(request, response with { SourceUserId = 11 }));
    }

    [Fact]
    public void BootstrapUriMustMatchSignedUserAndExactPath()
    {
        Assert.True(ProviderCommandProtocol.IsAccessUri("content://10@com.agnosia.app.commands/commands", 10));
        Assert.True(ProviderCommandProtocol.IsAccessUri("content://com.agnosia.app.commands/commands", 10));
        Assert.False(ProviderCommandProtocol.IsAccessUri("content://11@com.agnosia.app.commands/commands", 10));
        Assert.False(ProviderCommandProtocol.IsAccessUri("content://10@com.agnosia.app.commands/commands/other", 10));
        Assert.False(ProviderCommandProtocol.IsAccessUri("content://10@com.agnosia.app.commands/commands?x=1", 10));
    }

    [Fact]
    public void CallerAppIdMustMatchAcrossProfiles()
    {
        Assert.True(ProviderCallerIdentity.IsSameApp(1_010_399, 10_399));
        Assert.False(ProviderCallerIdentity.IsSameApp(1_010_400, 10_399));
        Assert.False(ProviderCallerIdentity.IsSameApp(-1, 10_399));
    }

    [Fact]
    public void OversizeWireIsRejectedBeforeDeserialization()
    {
        Assert.Throws<InvalidDataException>(() => ProviderCommandProtocol.Deserialize(new string('x', ProviderCommandProtocol.MaxMessageBytes)));
        var signed = ProviderCommandProtocol.Sign(Request(), Key);
        Assert.Equal(signed, ProviderCommandProtocol.Deserialize(ProviderCommandProtocol.Serialize(signed)));
    }

    [Fact]
    public async Task ReplayWithDifferentPayloadIsRejectedAndIdenticalCallExecutesOnce()
    {
        var store = new ProviderReplayStore();
        var request = Request();
        var count = 0;
        Task<ProviderCommandMessage> Run() { count++; return Task.FromResult(ProviderCommandProtocol.Reply(request, true, null, null, "done")); }
        var first = await store.ExecuteAsync(request, Run, 1500);
        Assert.Equal(first, await store.ExecuteAsync(request, Run, 1500));
        Assert.Equal(1, count);
        var conflict = await store.ExecuteAsync(request with { PayloadJson = "different" }, Run, 1500);
        Assert.Equal("replay_conflict", conflict.ErrorCode);
        Assert.Equal(1, count);
    }
}

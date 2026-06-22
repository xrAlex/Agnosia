using System.Text.Json;
using Agnosia.Android.Commands;
using Agnosia.Android.Api.Commands;
using Xunit;

namespace Agnosia.Unit.Android.Commands;

public sealed class AndroidCommandEnvelopeTests
{
    [Fact]
    public void TargetProfileActivityActions_HaveMatchingCommandKinds()
    {
        var commandKindNames = Enum.GetNames<AndroidCommandKind>().ToHashSet(StringComparer.Ordinal);
        var targetProfileActivityActions = AgnosiaActions.TargetProfileActivityActions.ToHashSet(StringComparer.Ordinal);

        var missingCommandKinds = typeof(AgnosiaActions)
            .GetFields()
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .Select(field => new
            {
                field.Name,
                Value = (string)field.GetRawConstantValue()!
            })
            .Where(action => targetProfileActivityActions.Contains(action.Value))
            .Select(action => action.Name)
            .Where(actionName => !commandKindNames.Contains(actionName))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(missingCommandKinds);
    }

    [Fact]
    public void CommandEnvelope_RoundTripsAllRoutingFields()
    {
        var envelope = new AndroidCommandEnvelope(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            AndroidCommandKind.QueryPermissions,
            AndroidCommandTargetProfile.Work,
            AndroidCommandInteractivity.Silent,
            AndroidCommandPriority.Refresh,
            TimeSpan.FromSeconds(7),
            """{"includeDetails":true}""");

        var json = JsonSerializer.Serialize(envelope);
        var roundTrip = JsonSerializer.Deserialize<AndroidCommandEnvelope>(json);

        Assert.NotNull(roundTrip);
        Assert.Equal(envelope.CorrelationId, roundTrip.CorrelationId);
        Assert.Equal(AndroidCommandKind.QueryPermissions, roundTrip.Kind);
        Assert.Equal(AndroidCommandTargetProfile.Work, roundTrip.TargetProfile);
        Assert.Equal(AndroidCommandInteractivity.Silent, roundTrip.Interactivity);
        Assert.Equal(AndroidCommandPriority.Refresh, roundTrip.Priority);
        Assert.Equal(TimeSpan.FromSeconds(7), roundTrip.Timeout);
        Assert.Equal("""{"includeDetails":true}""", roundTrip.PayloadJson);
    }

    [Fact]
    public void ResultEnvelope_RoundTripsDiagnosticsAndTransport()
    {
        var envelope = AndroidCommandResultEnvelope.Success(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            AndroidCommandKind.QueryLogs,
            AndroidCommandTransportKind.SilentService,
            """{"count":2}""",
            "Loaded logs.",
            TimeSpan.FromMilliseconds(42),
            "fallback=false");

        var json = JsonSerializer.Serialize(envelope);
        var roundTrip = JsonSerializer.Deserialize<AndroidCommandResultEnvelope>(json);

        Assert.NotNull(roundTrip);
        Assert.True(roundTrip.Succeeded);
        Assert.Equal(AndroidCommandKind.QueryLogs, roundTrip.Kind);
        Assert.Equal(AndroidCommandTransportKind.SilentService, roundTrip.Transport);
        Assert.Equal("""{"count":2}""", roundTrip.PayloadJson);
        Assert.Equal("Loaded logs.", roundTrip.Message);
        Assert.Equal("fallback=false", roundTrip.Diagnostics);
        Assert.Equal(TimeSpan.FromMilliseconds(42), roundTrip.Elapsed);
    }
}

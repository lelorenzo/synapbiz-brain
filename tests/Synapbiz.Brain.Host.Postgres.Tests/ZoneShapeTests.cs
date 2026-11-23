using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>
/// Section 10.3 of the W1-C specification: a zone id that does not have the shape <c>UTC</c> or <c>Area/Location</c> is refused
/// before the zone resolver is asked, whatever the resolver would have answered. The ids below exist in the time zone database of
/// many machines (<c>localtime</c>, <c>EST5EDT</c>, <c>Factory</c>) or would reach a file name (the path); the tests make the resolver
/// know every one of them, so that only the shape check can refuse them.
/// </summary>
public sealed class ZoneShapeTests
{
    public static TheoryData<string> BadZones => new()
    {
        "localtime",
        "EST5EDT",
        "Factory",
        "../../etc/passwd",
        "/etc/localtime",
        "America/New_York/../../x",
        "America/New York",
        "utc",
        "",
    };

    private static void Teach(Rig rig, string id)
    {
        if (id.Length > 0)
        {
            rig.Zones.Add(id, TestZoneResolver.Eastern);
        }
    }

    [Theory]
    [MemberData(nameof(BadZones))]
    public async Task AnActivationZoneOfTheWrongShapeIsRefusedWithoutAskingTheResolverAndTheMessageNeverQuotesIt(string zone)
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("trigger-schedule"));
        Teach(rig, zone);

        var act = () => rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, "host.trigger_schedule", zone));

        var failure = (await act.Should().ThrowAsync<ArgumentException>()).Which;
        failure.Message.Should().Contain("Zone");
        if (zone.Length > 0)
        {
            failure.Message.Should().NotContain(zone);
        }

        rig.Zones.Asked.Should().NotContain(zone);
        (await rig.CountAsync("process_activation")).Should().Be(0);
        (await rig.CountAsync("process_schedule")).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(BadZones))]
    public async Task AnActivationZoneOfTheWrongShapeIsRefusedEvenWhenNoScheduleNeedsIt(string zone)
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("trigger-event"));
        Teach(rig, zone);

        var act = () => rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, "host.trigger_event", zone, "{\"site\":\"s-1\"}"));

        await act.Should().ThrowAsync<ArgumentException>();
        (await rig.CountAsync("process_activation")).Should().Be(0);
        rig.Zones.Asked.Should().BeEmpty();
    }

    [Theory]
    [InlineData("UTC")]
    [InlineData("Test/Fixed")]
    [InlineData("America/New_York")]
    public async Task AnActivationZoneOfTheRightShapeIsAskedOfTheResolver(string zone)
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("trigger-schedule"));

        await rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, "host.trigger_schedule", zone));

        rig.Zones.Asked.Should().Contain(zone);
    }

    [Theory]
    [MemberData(nameof(BadZones))]
    public async Task AWindowZoneVariableOfTheWrongShapeFailsTheWindowWithoutAskingTheResolverAndNothingIsSent(string zone)
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("window-zone-variable"));
        var port = rig.Port("t.send");
        Teach(rig, zone);

        var id = await rig.StartAsync("host.window_zone", "k-1", System.Text.Json.JsonSerializer.Serialize(new { case_ref = "c-1", practice_zone = zone }));
        await rig.DrainAsync();

        port.Calls.Should().BeEmpty();
        var info = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
        info!.Result.Should().Be("failed", "the step went to onError");
        (await rig.Engine.GetHistoryAsync(rig.Tenant, id)).Should().Contain(entry => entry.Code == "process.window.zone_unknown");
        rig.Zones.Asked.Should().NotContain(zone);
        var warning = rig.Logs.Entries.Single(entry => entry.EventId.Id == 17);
        warning.Message.Should().StartWith("process.window.zone_unknown");
        if (zone.Length > 0)
        {
            warning.Message.Should().NotContain(zone);
        }

        (await rig.OutstandingWorkAsync(id)).Should().Be(0);
    }

    [Fact]
    public async Task AWindowZoneVariableOfTheWrongShapeSuspendsTheInstanceWhenTheStepHasNoOnError()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("window-zone-suspend"));
        rig.Port("t.send");
        rig.Zones.Add("localtime", TestZoneResolver.Eastern);

        var id = await rig.StartAsync("host.window_suspend", "k-1", "{\"case_ref\":\"c-1\",\"practice_zone\":\"localtime\"}");
        await rig.DrainAsync();

        var info = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
        info!.Status.Should().Be(ProcessStatus.Suspended);
        info.Problem!.Code.Should().Be("process.window.zone_unknown");
        rig.Zones.Asked.Should().NotContain("localtime");
    }
}

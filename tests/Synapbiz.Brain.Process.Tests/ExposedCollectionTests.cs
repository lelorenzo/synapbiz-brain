using System.Collections;
using FluentAssertions;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>Section 10.7 of the W1-C specification: what the definition exposes cannot be used to change it.</summary>
public sealed class ExposedCollectionTests
{
    private const string Variables = """
        {
          "case_ref": { "type": "Code", "input": true },
          "visit_date": { "type": "Date", "input": true },
          "fired_at": { "type": "Instant", "input": true }
        }
        """;

    private const string Extra = """
        "triggers": [
          { "kind": "event", "event": "case.opened", "businessKey": "case_ref", "input": { "case_ref": "case_ref", "visit_date": "visit_date" } },
          { "kind": "schedule", "id": "daily", "cron": "0 6 * * *", "zone": "America/New_York", "misfire": "skip", "input": { "fired_at": "scheduledFor" } }
        ],
        "windows": {
          "hours": { "zone": "America/New_York", "days": ["mon", "tue", "wed", "thu", "fri"], "from": "09:00", "to": "18:00" }
        }
        """;

    private const string Steps = """
        {
          "s": { "kind": "action", "port": "messaging.notice", "delivery": "atLeastOnce", "contact": true, "window": "hours", "input": { "case": "case_ref" }, "next": "ok" },
          "ok": { "kind": "end", "result": "ok" }
        }
        """;

    private static BrainProcess Process() => Docs.Load(Variables, Steps, Extra);

    private static void CannotBeChanged<T>(object collection, T item)
    {
        collection.Should().NotBeAssignableTo<List<T>>();
        collection.Should().NotBeAssignableTo<T[]>();
        var list = collection as IList<T>;
        if (list is not null)
        {
            list.IsReadOnly.Should().BeTrue();
            var add = () => list.Add(item);
            add.Should().Throw<NotSupportedException>();
            var clear = () => list.Clear();
            clear.Should().Throw<NotSupportedException>();
            if (list.Count > 0)
            {
                var replace = () => list[0] = item;
                replace.Should().Throw<NotSupportedException>();
            }
        }
    }

    private static void CannotBeChanged(IReadOnlyDictionary<string, string> map)
    {
        map.Should().NotBeAssignableTo<Dictionary<string, string>>();
        map.Should().NotBeAssignableTo<SortedDictionary<string, string>>();
        var dictionary = map as IDictionary<string, string>;
        dictionary.Should().NotBeNull();
        dictionary!.IsReadOnly.Should().BeTrue();
        var add = () => dictionary.Add("zzz", "zzz");
        add.Should().Throw<NotSupportedException>();
        var set = () => dictionary[map.Keys.First()] = "changed";
        set.Should().Throw<NotSupportedException>();
        var remove = () => dictionary.Remove(map.Keys.First());
        remove.Should().Throw<NotSupportedException>();
        (map as IDictionary).Should().NotBeNull();
        (map as IDictionary)!.IsReadOnly.Should().BeTrue();
    }

    [Fact]
    public void TheTriggerListCannotBeChangedThroughACast()
    {
        var process = Process();
        var first = process.Triggers[0];

        CannotBeChanged(process.Triggers, first);

        process.Triggers.Should().HaveCount(2);
        process.Triggers[0].Should().BeSameAs(first);
    }

    [Fact]
    public void TheInputMapOfEveryTriggerCannotBeChangedThroughACastAndKeepsItsOrder()
    {
        var process = Process();
        var before = process.Triggers.Select(trigger => string.Join(",", trigger.Input.Select(pair => pair.Key + "=" + pair.Value))).ToList();

        foreach (var trigger in process.Triggers)
        {
            CannotBeChanged(trigger.Input);
        }

        before.Should().Equal("case_ref=case_ref,visit_date=visit_date", "fired_at=scheduledFor");
        process.Triggers.Select(trigger => string.Join(",", trigger.Input.Select(pair => pair.Key + "=" + pair.Value))).Should().Equal(before);
    }

    [Fact]
    public void TheDaysOfAWindowRequestCannotBeChangedThroughACastAndTheNextRequestIsUnaffected()
    {
        var process = Process();
        var driver = new Driver(process);
        driver.Start("""{ "case_ref": "c-1", "visit_date": "2027-02-01", "fired_at": "2027-01-04T14:00:00Z" }""");
        var request = driver.Single<WindowRequest>();

        CannotBeChanged(request.Days, DayOfWeek.Sunday);

        request.Days.Should().Equal(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday);
        var again = new Driver(process);
        again.Start("""{ "case_ref": "c-2", "visit_date": "2027-02-01", "fired_at": "2027-01-04T14:00:00Z" }""");
        again.Single<WindowRequest>().Days.Should().Equal(request.Days);
    }

    [Fact]
    public void AWindowRequestCopiesTheDaysItIsGiven()
    {
        var days = new List<DayOfWeek> { DayOfWeek.Monday };

        var request = new WindowRequest("s", "t", "UTC", days, new TimeOnly(9, 0), new TimeOnly(10, 0));
        days.Add(DayOfWeek.Friday);

        request.Days.Should().Equal(DayOfWeek.Monday);
        CannotBeChanged(request.Days, DayOfWeek.Sunday);
    }

    [Theory]
    [InlineData("UTC", true)]
    [InlineData("America/New_York", true)]
    [InlineData("America/Argentina/Buenos_Aires", true)]
    [InlineData("Etc/GMT+5", true)]
    [InlineData("localtime", false)]
    [InlineData("EST5EDT", false)]
    [InlineData("Factory", false)]
    [InlineData("utc", false)]
    [InlineData("../../etc/passwd", false)]
    [InlineData("/etc/localtime", false)]
    [InlineData("America/New_York/../../x", false)]
    [InlineData("America/New York", false)]
    [InlineData("America/New_York\n", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TheZoneIdShapeIsUtcOrAreaSlashLocation(string? text, bool expected)
    {
        DeliveryWindows.IsZoneIdShape(text).Should().Be(expected);
    }
}

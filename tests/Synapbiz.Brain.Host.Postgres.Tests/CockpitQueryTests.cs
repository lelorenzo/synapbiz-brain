using System.Buffers.Text;
using System.Globalization;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>
/// The read-only cockpit queries: list, count, case and children. Most of the data is written straight into the tables, so that a
/// test controls the status, the parent link and the creation instant of every row; the reference case drives the real engine.
/// </summary>
public sealed class CockpitQueryTests
{
    private const string A = "tenant-a";
    private const string B = "tenant-b";
    private const int Created = 0;
    private const int Running = 1;
    private const int Waiting = 2;
    private const int Suspended = 3;
    private const int Completed = 4;

    private static readonly DateTimeOffset T0 = new(2027, 2, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EveryQueryAnswersEmptyOnAnEmptyDatabase()
    {
        await using var rig = await Rig.CreateAsync();

        var page = await rig.Engine.ListInstancesAsync(new ProcessInstanceQuery(A));
        page.Items.Should().BeEmpty();
        page.Next.Should().BeNull();
        (await rig.Engine.CountInstancesAsync(A, null, false)).Should().BeEmpty();
        (await rig.Engine.GetCaseAsync(A, "nope")).Should().BeNull();
        (await rig.Engine.ListChildrenAsync(A, "nope")).Should().BeEmpty();
    }

    [Fact]
    public async Task ArgumentsAreCheckedWithTheGuardOfTheOtherMethods()
    {
        await using var rig = await Rig.CreateAsync();
        var engine = rig.Engine;
        var tooLong = new string('x', 201);

        await FluentActions.Awaiting(() => engine.ListInstancesAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => engine.ListInstancesAsync(new ProcessInstanceQuery(null!))).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => engine.ListInstancesAsync(new ProcessInstanceQuery(""))).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => engine.ListInstancesAsync(new ProcessInstanceQuery(tooLong))).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => engine.ListInstancesAsync(new ProcessInstanceQuery(A, ProcessId: ""))).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => engine.CountInstancesAsync(null!, null, false)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => engine.CountInstancesAsync("", null, false)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => engine.CountInstancesAsync(A, tooLong, false)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => engine.GetCaseAsync(null!, "i")).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => engine.GetCaseAsync(A, null!)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => engine.GetCaseAsync(A, "")).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => engine.GetCaseAsync(A, tooLong)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => engine.ListChildrenAsync(A, "")).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => engine.ListChildrenAsync("", "i")).Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ATenantNeverSeesAnotherTenantsRowsEvenByInstanceId()
    {
        await using var rig = await Rig.CreateAsync();
        foreach (var tenant in new[] { A, B })
        {
            await AddInstance(rig, tenant, tenant + "-root", "p.one", "k-1", Waiting, "s1", created: T0);
            await AddInstance(rig, tenant, tenant + "-child", "p.one", tenant + "-root/c", Suspended, "s2", tenant + "-root", T0.AddMinutes(1));
            await AddTask(rig, tenant, tenant + "-task", tenant + "-child", T0.AddMinutes(2));
            await AddTimer(rig, tenant, tenant + "-timer", tenant + "-child", T0.AddHours(1));
            await AddWait(rig, tenant, tenant + "-wait", tenant + "-child", "ev." + tenant);
        }

        var page = await rig.Engine.ListInstancesAsync(new ProcessInstanceQuery(A, RootsOnly: false));
        var counts = await rig.Engine.CountInstancesAsync(A, null, false);
        var own = await rig.Engine.GetCaseAsync(A, A + "-root");

        page.Items.Select(item => item.InstanceId).Should().Equal(A + "-child", A + "-root");
        page.Items.Should().OnlyContain(item => item.TenantId == A);
        counts.Select(count => (count.ProcessId, count.Status, count.Stage, count.Count)).Should().Equal(
            ("p.one", ProcessStatus.Waiting, "s1", 1),
            ("p.one", ProcessStatus.Suspended, "s2", 1));
        own!.Descendants.Should().ContainSingle().Which.TenantId.Should().Be(A);
        own.OpenTasks.Should().ContainSingle().Which.TenantId.Should().Be(A);
        own.WaitingForEvents.Should().Equal("ev." + A);
        own.NextTimerDue.Should().Be(T0.AddHours(1));
        (await rig.Engine.GetCaseAsync(A, B + "-root")).Should().BeNull();
        (await rig.Engine.GetCaseAsync(A, B + "-child")).Should().BeNull();
        (await rig.Engine.ListChildrenAsync(A, B + "-root")).Should().BeEmpty();
        (await rig.Engine.ListChildrenAsync(A, A + "-root")).Should().ContainSingle().Which.InstanceId.Should().Be(A + "-child");
    }

    [Fact]
    public async Task EveryFilterWorksAloneAndCombined()
    {
        await using var rig = await Rig.CreateAsync();
        await AddInstance(rig, A, "r1", "p.one", "k1", Waiting, "intake", created: T0.AddMinutes(1));
        await AddInstance(rig, A, "r2", "p.one", "k2", Completed, "done", created: T0.AddMinutes(2));
        await AddInstance(rig, A, "r3", "p.two", "k3", Waiting, "intake", created: T0.AddMinutes(3));
        await AddInstance(rig, A, "r4", "p.two", "k4", Suspended, "Intake", created: T0.AddMinutes(4));
        await AddInstance(rig, A, "c1", "p.one", "k5", Waiting, "intake", "r1", T0.AddMinutes(5));

        async Task<string[]> Ids(ProcessInstanceQuery query) => [.. (await rig.Engine.ListInstancesAsync(query)).Items.Select(item => item.InstanceId)];

        (await Ids(new ProcessInstanceQuery(A))).Should().Equal("r4", "r3", "r2", "r1");
        (await Ids(new ProcessInstanceQuery(A, RootsOnly: false))).Should().Equal("c1", "r4", "r3", "r2", "r1");
        (await Ids(new ProcessInstanceQuery(A, ProcessId: "p.one", RootsOnly: false))).Should().Equal("c1", "r2", "r1");
        (await Ids(new ProcessInstanceQuery(A, ProcessId: "p.none"))).Should().BeEmpty();
        (await Ids(new ProcessInstanceQuery(A, Statuses: [ProcessStatus.Waiting]))).Should().Equal("r3", "r1");
        (await Ids(new ProcessInstanceQuery(A, Statuses: [ProcessStatus.Waiting, ProcessStatus.Completed]))).Should().Equal("r3", "r2", "r1");
        (await Ids(new ProcessInstanceQuery(A, Statuses: []))).Should().Equal("r4", "r3", "r2", "r1");
        (await Ids(new ProcessInstanceQuery(A, Stage: "intake", RootsOnly: false))).Should().Equal("c1", "r3", "r1");
        (await Ids(new ProcessInstanceQuery(A, Stage: "INTAKE"))).Should().BeEmpty("the stage is compared exactly, case included");
        (await Ids(new ProcessInstanceQuery(A, ProcessId: "p.two", Statuses: [ProcessStatus.Waiting], Stage: "intake", RootsOnly: false))).Should().Equal("r3");
        (await Ids(new ProcessInstanceQuery(A, ProcessId: "p.one", Statuses: [ProcessStatus.Waiting], Stage: "intake", RootsOnly: true))).Should().Equal("r1");
    }

    [Fact]
    public async Task NeedsAttentionKeepsSuspendedInstancesSuspendedDescendantsAndOpenTasksOfTheTree()
    {
        await using var rig = await Rig.CreateAsync();
        await AddInstance(rig, A, "quiet", "p", "k0", Waiting, created: T0.AddMinutes(1));
        await AddInstance(rig, A, "self", "p", "k1", Suspended, created: T0.AddMinutes(2));
        await AddInstance(rig, A, "via-child", "p", "k2", Waiting, created: T0.AddMinutes(3));
        await AddInstance(rig, A, "via-child/c", "p", "k2c", Suspended, parent: "via-child", created: T0.AddMinutes(4));
        await AddInstance(rig, A, "own-task", "p", "k3", Waiting, created: T0.AddMinutes(5));
        await AddTask(rig, A, "t-own", "own-task", T0);
        await AddInstance(rig, A, "deep-task", "p", "k4", Waiting, created: T0.AddMinutes(6));
        await AddInstance(rig, A, "deep-task/c", "p", "k4c", Waiting, parent: "deep-task", created: T0.AddMinutes(7));
        await AddInstance(rig, A, "deep-task/c/g", "p", "k4g", Waiting, parent: "deep-task/c", created: T0.AddMinutes(8));
        await AddTask(rig, A, "t-deep", "deep-task/c/g", T0);
        await AddInstance(rig, A, "closed-task", "p", "k5", Completed, created: T0.AddMinutes(9));
        await AddTask(rig, A, "t-closed", "closed-task", T0, status: 2);

        var attention = await rig.Engine.ListInstancesAsync(new ProcessInstanceQuery(A, NeedsAttention: true));
        var all = await rig.Engine.ListInstancesAsync(new ProcessInstanceQuery(A));

        attention.Items.Select(item => item.InstanceId).Should().Equal("deep-task", "own-task", "via-child", "self");
        all.Items.Select(item => item.InstanceId).Should().Equal("closed-task", "deep-task", "own-task", "via-child", "self", "quiet");
        (await rig.Engine.ListInstancesAsync(new ProcessInstanceQuery(A, NeedsAttention: true, RootsOnly: false))).Items.Select(item => item.InstanceId)
            .Should().Equal("deep-task/c/g", "deep-task/c", "deep-task", "own-task", "via-child/c", "via-child", "self");
        (await rig.Engine.ListInstancesAsync(new ProcessInstanceQuery(A, NeedsAttention: true, Statuses: [ProcessStatus.Suspended]))).Items.Select(item => item.InstanceId)
            .Should().Equal("self");
    }

    [Fact]
    public async Task AttentionFilteringIsAppliedBeforeTheLimitSoPagesAreFull()
    {
        await using var rig = await Rig.CreateAsync();
        for (var i = 0; i < 6; i++)
        {
            await AddInstance(rig, A, "q" + i, "p", "kq" + i, Waiting, created: T0.AddMinutes(10 + i));
        }

        await AddInstance(rig, A, "s1", "p", "ks1", Suspended, created: T0.AddMinutes(1));
        await AddInstance(rig, A, "s2", "p", "ks2", Suspended, created: T0.AddMinutes(2));

        var first = await rig.Engine.ListInstancesAsync(new ProcessInstanceQuery(A, NeedsAttention: true, Limit: 1));
        var second = await rig.Engine.ListInstancesAsync(new ProcessInstanceQuery(A, NeedsAttention: true, Limit: 1, After: first.Next));

        first.Items.Select(item => item.InstanceId).Should().Equal("s2");
        first.Next.Should().NotBeNull();
        second.Items.Select(item => item.InstanceId).Should().Equal("s1");
        second.Next.Should().BeNull();
    }

    [Fact]
    public async Task PagingReturnsEveryRowOnceInOrderEvenWhenCreationInstantsAreEqual()
    {
        await using var rig = await Rig.CreateAsync();
        var sameInstant = T0.AddTicks(1234560);
        var rows = new List<(string Id, DateTimeOffset At)>();
        for (var i = 0; i < 10; i++)
        {
            // Three groups of rows share an instant; one group has microseconds that a millisecond-rounding cursor would lose.
            var instant = T0.AddMinutes(5);
            if (i < 8)
            {
                instant = i < 4 ? T0 : sameInstant;
            }

            var id = "i-" + i.ToString("00", CultureInfo.InvariantCulture);
            await AddInstance(rig, A, id, "p", "k-" + id, Waiting, created: instant);
            rows.Add((id, instant));
        }

        var expected = rows.OrderByDescending(row => row.At).ThenByDescending(row => row.Id, StringComparer.Ordinal).Select(row => row.Id).ToList();

        foreach (var limit in PageSizes)
        {
            var seen = new List<string>();
            string? cursor = null;
            var pages = 0;
            do
            {
                var page = await rig.Engine.ListInstancesAsync(new ProcessInstanceQuery(A, Limit: limit, After: cursor));
                page.Items.Count.Should().BeLessThanOrEqualTo(limit);
                seen.AddRange(page.Items.Select(item => item.InstanceId));
                cursor = page.Next;
                pages++;
            }
            while (cursor is not null && pages < 50);

            seen.Should().Equal(expected, "limit " + limit);
            pages.Should().Be((int)Math.Ceiling(10d / limit), "a last page that is exactly full has no next cursor");
        }
    }

    [Fact]
    public async Task TheCursorCarriesOnlyTheInstantAndTheIdOfTheLastRow()
    {
        await using var rig = await Rig.CreateAsync();
        var instant = T0.AddTicks(5678900);
        await AddInstance(rig, A, "x-1", "p", "k1", Waiting, created: instant);
        await AddInstance(rig, A, "x-2", "p", "k2", Waiting, created: instant);

        var page = await rig.Engine.ListInstancesAsync(new ProcessInstanceQuery(A, Limit: 1));

        page.Items.Single().InstanceId.Should().Be("x-2");
        Encoding.UTF8.GetString(Base64Url.DecodeFromChars(page.Next!)).Should().Be(instant.UtcTicks.ToString(CultureInfo.InvariantCulture) + "|x-2");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(501)]
    public async Task ALimitOutsideOneToFiveHundredIsRejected(int limit)
    {
        await using var rig = await Rig.CreateAsync();

        await FluentActions.Awaiting(() => rig.Engine.ListInstancesAsync(new ProcessInstanceQuery(A, Limit: limit))).Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(500)]
    public async Task TheLimitBoundsThemselvesAreAccepted(int limit)
    {
        await using var rig = await Rig.CreateAsync();
        await AddInstance(rig, A, "x", "p", "k", Waiting, created: T0);

        (await rig.Engine.ListInstancesAsync(new ProcessInstanceQuery(A, Limit: limit))).Items.Should().ContainSingle();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a cursor!")]
    [InlineData("YWJj")]
    [InlineData("eHx5")]
    [InlineData("MTIzfA")]
    [InlineData("LTF8eA")]
    [InlineData("OTk5OTk5OTk5OTk5OTk5OTk5OTk5fHg")]
    [InlineData("MXwA")]
    public async Task ACursorThatDoesNotParseIsAnArgumentException(string cursor)
    {
        await using var rig = await Rig.CreateAsync();

        await FluentActions.Awaiting(() => rig.Engine.ListInstancesAsync(new ProcessInstanceQuery(A, After: cursor))).Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ACursorWhoseIdHoldsAControlCharacterIsRejected()
    {
        await using var rig = await Rig.CreateAsync();
        foreach (var id in new[] { "a\0b", "\0", "a\nb", "a\u007fb" })
        {
            var cursor = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("1|" + id));

            await FluentActions.Awaiting(() => rig.Engine.ListInstancesAsync(new ProcessInstanceQuery(A, After: cursor))).Should().ThrowAsync<ArgumentException>();
        }
    }

    [Fact]
    public async Task ACursorLongerThanTheBoundIsRejectedAndTheLongestRealCursorIsAccepted()
    {
        await using var rig = await Rig.CreateAsync();
        var longestId = new string('z', Guard.MaxIdLength);
        await AddInstance(rig, A, longestId, "p", "k", Waiting, created: T0);
        var longest = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("1|" + longestId));

        InstanceCursors.MaxLength.Should().Be((4 * Guard.MaxIdLength) + 64);
        longest.Length.Should().BeLessThan(InstanceCursors.MaxLength);
        (await rig.Engine.ListInstancesAsync(new ProcessInstanceQuery(A, After: longest))).Items.Should().BeEmpty("the one row is newer than the instant 1 of the cursor");
        await FluentActions.Awaiting(() => rig.Engine.ListInstancesAsync(new ProcessInstanceQuery(A, After: new string('A', InstanceCursors.MaxLength + 1)))).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => rig.Engine.ListInstancesAsync(new ProcessInstanceQuery(A, After: new string('A', 5_000_000)))).Should().ThrowAsync<ArgumentException>();
        InstanceCursors.Parse(longest).InstanceId.Should().Be(longestId);
    }

    [Fact]
    public async Task AControlCharacterInATenantProcessStageOrInstanceIdIsAnArgumentException()
    {
        await using var rig = await Rig.CreateAsync();
        var engine = rig.Engine;
        var bad = new[] { "a\0b", "\0", "a\nb" };

        foreach (var value in bad)
        {
            await FluentActions.Awaiting(() => engine.ListInstancesAsync(new ProcessInstanceQuery(A, Stage: value))).Should().ThrowAsync<ArgumentException>();
            await FluentActions.Awaiting(() => engine.ListInstancesAsync(new ProcessInstanceQuery(A, ProcessId: value))).Should().ThrowAsync<ArgumentException>();
            await FluentActions.Awaiting(() => engine.ListInstancesAsync(new ProcessInstanceQuery(value))).Should().ThrowAsync<ArgumentException>();
            await FluentActions.Awaiting(() => engine.CountInstancesAsync(value, null, false)).Should().ThrowAsync<ArgumentException>();
            await FluentActions.Awaiting(() => engine.CountInstancesAsync(A, value, false)).Should().ThrowAsync<ArgumentException>();
            await FluentActions.Awaiting(() => engine.GetCaseAsync(value, "i")).Should().ThrowAsync<ArgumentException>();
            await FluentActions.Awaiting(() => engine.GetCaseAsync(A, value)).Should().ThrowAsync<ArgumentException>();
            await FluentActions.Awaiting(() => engine.ListChildrenAsync(value, "i")).Should().ThrowAsync<ArgumentException>();
            await FluentActions.Awaiting(() => engine.ListChildrenAsync(A, value)).Should().ThrowAsync<ArgumentException>();
        }

        (await engine.ListInstancesAsync(new ProcessInstanceQuery(A, Stage: "a b"))).Items.Should().BeEmpty("an ordinary space is not a control character");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public async Task TheLimitErrorNamesTheLimit(int limit)
    {
        await using var rig = await Rig.CreateAsync();

        var thrown = await FluentActions.Awaiting(() => rig.Engine.ListInstancesAsync(new ProcessInstanceQuery(A, Limit: limit))).Should().ThrowAsync<ArgumentOutOfRangeException>();

        thrown.Which.ParamName.Should().Be("Limit");
        thrown.Which.ActualValue.Should().Be(limit);
    }

    [Fact]
    public async Task TenantsWithTheSameInstanceAndParentIdsNeverSeeEachOthersTrees()
    {
        await using var rig = await Rig.CreateAsync();
        foreach (var (id, parent, status) in new[] { ("root", (string?)null, Waiting), ("child", "root", Suspended), ("grand", "child", Suspended) })
        {
            await AddInstance(rig, B, id, "p", "k-" + id, status, "s", parent, T0);
            await AddTask(rig, B, "t-" + id, id, T0);
        }

        await AddTimer(rig, B, "tm-1", "grand", T0.AddHours(1));
        await AddTimer(rig, B, "tm-2", "root", T0.AddHours(2));
        await AddWait(rig, B, "w-1", "child", "ev.b");
        await AddWait(rig, B, "w-2", "grand", "ev.b2");

        // Tenant A: the same ids, the same parent links, and nothing that needs attention.
        foreach (var (id, parent) in new[] { ("root", (string?)null), ("child", "root"), ("grand", "child") })
        {
            await AddInstance(rig, A, id, "p", "k-" + id, Waiting, "s", parent, T0);
        }

        var engine = rig.Engine;
        var roots = await engine.ListInstancesAsync(new ProcessInstanceQuery(A));
        var all = await engine.ListInstancesAsync(new ProcessInstanceQuery(A, RootsOnly: false));
        var attention = await engine.ListInstancesAsync(new ProcessInstanceQuery(A, NeedsAttention: true, RootsOnly: false));
        var counts = await engine.CountInstancesAsync(A, null, false);
        var rootCase = (await engine.GetCaseAsync(A, "root"))!;
        var childCase = (await engine.GetCaseAsync(A, "child"))!;

        roots.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(new { TenantId = A, InstanceId = "root", Status = ProcessStatus.Waiting, OpenTasks = 0, SuspendedDescendants = 0 });
        all.Items.Should().HaveCount(3).And.OnlyContain(item => item.TenantId == A && item.OpenTasks == 0 && item.SuspendedDescendants == 0 && item.Status == ProcessStatus.Waiting);
        attention.Items.Should().BeEmpty();
        (await engine.ListInstancesAsync(new ProcessInstanceQuery(A, NeedsAttention: true))).Items.Should().BeEmpty();
        counts.Should().Equal(new ProcessInstanceCount("p", ProcessStatus.Waiting, "s", 3));
        foreach (var view in new[] { rootCase, childCase })
        {
            view.Instance.Should().BeEquivalentTo(new { TenantId = A, OpenTasks = 0, SuspendedDescendants = 0 });
            view.Descendants.Should().OnlyContain(item => item.TenantId == A && item.OpenTasks == 0 && item.SuspendedDescendants == 0);
            view.OpenTasks.Should().BeEmpty();
            view.NextTimerDue.Should().BeNull();
            view.WaitingForEvents.Should().BeEmpty();
        }

        rootCase.Descendants.Select(item => item.InstanceId).Should().Equal("child", "grand");
        childCase.Descendants.Select(item => item.InstanceId).Should().Equal("grand");
        (await engine.ListChildrenAsync(A, "root")).Should().ContainSingle().Which.Should().BeEquivalentTo(new { TenantId = A, InstanceId = "child", OpenTasks = 0, SuspendedDescendants = 0 });
        (await engine.ListChildrenAsync(A, "child")).Should().ContainSingle().Which.InstanceId.Should().Be("grand");

        // The fixture is not vacuous: tenant B sees all of what A must not see.
        var bCase = (await engine.GetCaseAsync(B, "root"))!;
        bCase.Instance.SuspendedDescendants.Should().Be(2);
        bCase.OpenTasks.Should().HaveCount(3);
        bCase.NextTimerDue.Should().Be(T0.AddHours(1));
        bCase.WaitingForEvents.Should().Equal("ev.b", "ev.b2");
        (await engine.ListInstancesAsync(new ProcessInstanceQuery(B, NeedsAttention: true, RootsOnly: false))).Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task AChildOfAnotherTenantNeverJoinsTheWalkOfAnInstanceWithTheSameId()
    {
        await using var rig = await Rig.CreateAsync();
        await AddInstance(rig, B, "root", "p", "k-root", Waiting, created: T0);
        await AddInstance(rig, B, "x", "p", "k-x", Waiting, parent: "root", created: T0.AddMinutes(1));
        await AddInstance(rig, A, "root", "p", "k-root", Waiting, created: T0);
        await AddInstance(rig, A, "x", "p", "k-x", Suspended, created: T0.AddMinutes(1));

        var view = (await rig.Engine.GetCaseAsync(A, "root"))!;
        var all = (await rig.Engine.ListInstancesAsync(new ProcessInstanceQuery(A, RootsOnly: false))).Items.ToDictionary(item => item.InstanceId);
        var attention = await rig.Engine.ListInstancesAsync(new ProcessInstanceQuery(A, NeedsAttention: true));

        view.Descendants.Should().BeEmpty();
        view.Instance.SuspendedDescendants.Should().Be(0);
        all["root"].SuspendedDescendants.Should().Be(0);
        all["x"].Status.Should().Be(ProcessStatus.Suspended);
        attention.Items.Select(item => item.InstanceId).Should().Equal("x");
        (await rig.Engine.ListChildrenAsync(A, "root")).Should().BeEmpty();
        (await rig.Engine.GetCaseAsync(B, "root"))!.Descendants.Should().ContainSingle().Which.InstanceId.Should().Be("x");
    }

    [Fact]
    public async Task TheCaseIsOneSnapshotWhateverAnotherSessionCommitsBetweenItsReads()
    {
        await using var rig = await Rig.CreateAsync();
        await AddInstance(rig, A, "root", "p", "k0", Waiting, created: T0);
        await AddInstance(rig, A, "child", "p", "k1", Waiting, parent: "root", created: T0.AddMinutes(1));
        var store = rig.Provider.GetRequiredService<PostgresProcessStore>();
        store.AfterCaseSummaries = async (_, _) =>
        {
            // Committed from another connection after the first read of the case: none of it may show.
            await AddInstance(rig, A, "late", "p", "k2", Suspended, parent: "root", created: T0.AddMinutes(2));
            await AddTask(rig, A, "t-late", "child", T0);
            await AddTimer(rig, A, "tm-late", "child", T0.AddHours(1));
            await AddWait(rig, A, "w-late", "child", "ev.late");
        };

        var view = (await rig.Engine.GetCaseAsync(A, "root"))!;
        store.AfterCaseSummaries = null;
        var after = (await rig.Engine.GetCaseAsync(A, "root"))!;

        view.Descendants.Select(item => item.InstanceId).Should().Equal("child");
        view.Instance.SuspendedDescendants.Should().Be(0);
        view.OpenTasks.Should().BeEmpty();
        view.NextTimerDue.Should().BeNull();
        view.WaitingForEvents.Should().BeEmpty();
        after.Descendants.Select(item => item.InstanceId).Should().Equal("child", "late");
        after.OpenTasks.Should().ContainSingle();
        after.NextTimerDue.Should().Be(T0.AddHours(1));
        after.WaitingForEvents.Should().Equal("ev.late");
    }

    [Fact]
    public async Task TheCaseRunsInOneReadOnlyTransactionAtRepeatableRead()
    {
        await using var rig = await Rig.CreateAsync();
        await AddInstance(rig, A, "root", "p", "k0", Waiting, created: T0);
        var store = rig.Provider.GetRequiredService<PostgresProcessStore>();
        string? isolation = null;
        string? readOnly = null;
        var calls = 0;
        store.AfterCaseSummaries = async (connection, transaction) =>
        {
            calls++;
            await using var command = new Npgsql.NpgsqlCommand("select current_setting('transaction_isolation'), current_setting('transaction_read_only')", connection, transaction);
            await using var reader = await command.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            isolation = reader.GetString(0);
            readOnly = reader.GetString(1);
        };

        (await rig.Engine.GetCaseAsync(A, "root")).Should().NotBeNull();

        calls.Should().Be(1);
        isolation.Should().Be("repeatable read");
        readOnly.Should().Be("on");
    }

    [Theory]
    [InlineData(17, 0)]
    [InlineData(16, 1)]
    [InlineData(1, 1)]
    public async Task ASuspendedInstanceAndAnOpenTaskCountOnlyToDepthSixteenBelowTheRoot(int depth, int expected)
    {
        await using var rig = await Rig.CreateAsync();
        await AddInstance(rig, A, "n0", "p", "k0", Waiting, created: T0);
        for (var level = 1; level <= depth; level++)
        {
            await AddInstance(rig, A, "n" + level, "p", "k" + level, level == depth ? Suspended : Waiting, parent: "n" + (level - 1), created: T0.AddMinutes(level));
        }

        await AddTask(rig, A, "t-deepest", "n" + depth, T0);

        var view = (await rig.Engine.GetCaseAsync(A, "n0"))!;
        var attention = await rig.Engine.ListInstancesAsync(new ProcessInstanceQuery(A, NeedsAttention: true));
        var listed = (await rig.Engine.ListInstancesAsync(new ProcessInstanceQuery(A))).Items.Single();

        view.Instance.SuspendedDescendants.Should().Be(expected);
        listed.SuspendedDescendants.Should().Be(expected);
        view.Descendants.Should().HaveCount(Math.Min(depth, 16));
        view.OpenTasks.Should().HaveCount(expected);
        attention.Items.Should().HaveCount(expected);
    }

    [Fact]
    public async Task ACreatedInstanceCountsAndFiltersAsRunning()
    {
        await using var rig = await Rig.CreateAsync();
        await AddInstance(rig, A, "r", "p", "k1", Running, "s", created: T0);
        await AddInstance(rig, A, "c", "p", "k2", Created, "s", created: T0.AddMinutes(1));
        await AddInstance(rig, A, "w", "p", "k3", Waiting, "s", created: T0.AddMinutes(2));
        await AddInstance(rig, A, "n", "p", "k4", Created, null, created: T0.AddMinutes(3));

        var running = await rig.Engine.ListInstancesAsync(new ProcessInstanceQuery(A, Statuses: [ProcessStatus.Running]));
        var counts = await rig.Engine.CountInstancesAsync(A, null, false);

        running.Items.Select(item => item.InstanceId).Should().Equal("n", "c", "r");
        running.Items.Should().OnlyContain(item => item.Status == ProcessStatus.Running);
        counts.Select(count => (count.Status, count.Stage, count.Count)).Should().Equal(
            (ProcessStatus.Running, null, 1),
            (ProcessStatus.Running, "s", 2),
            (ProcessStatus.Waiting, "s", 1));
    }

    [Fact]
    public async Task CountsGroupByProcessStatusAndStageInOrder()
    {
        await using var rig = await Rig.CreateAsync();
        await AddInstance(rig, A, "1", "b.proc", "k1", Waiting, "z", created: T0);
        await AddInstance(rig, A, "2", "b.proc", "k2", Waiting, "z", created: T0);
        await AddInstance(rig, A, "3", "b.proc", "k3", Waiting, null, created: T0);
        await AddInstance(rig, A, "4", "b.proc", "k4", Waiting, "a", created: T0);
        await AddInstance(rig, A, "5", "b.proc", "k5", Completed, "a", created: T0);
        await AddInstance(rig, A, "6", "a.proc", "k6", Suspended, null, created: T0);
        await AddInstance(rig, A, "6/c", "a.proc", "k7", Waiting, "a", parent: "6", created: T0);
        await AddInstance(rig, B, "7", "a.proc", "k6", Waiting, null, created: T0);

        var all = await rig.Engine.CountInstancesAsync(A, null, false);
        var roots = await rig.Engine.CountInstancesAsync(A, null, true);
        var one = await rig.Engine.CountInstancesAsync(A, "b.proc", false);

        all.Should().Equal(
            new ProcessInstanceCount("a.proc", ProcessStatus.Waiting, "a", 1),
            new ProcessInstanceCount("a.proc", ProcessStatus.Suspended, null, 1),
            new ProcessInstanceCount("b.proc", ProcessStatus.Waiting, null, 1),
            new ProcessInstanceCount("b.proc", ProcessStatus.Waiting, "a", 1),
            new ProcessInstanceCount("b.proc", ProcessStatus.Waiting, "z", 2),
            new ProcessInstanceCount("b.proc", ProcessStatus.Completed, "a", 1));
        roots.Should().NotContain(count => count.ProcessId == "a.proc" && count.Stage == "a");
        roots.Sum(count => count.Count).Should().Be(6);
        one.Sum(count => count.Count).Should().Be(5);
        one.Should().OnlyContain(count => count.ProcessId == "b.proc");
    }

    [Fact]
    public async Task SuspendedDescendantsAndOpenTasksCountTheTreeBelowEachRow()
    {
        await using var rig = await Rig.CreateAsync();
        await AddInstance(rig, A, "root", "p", "k0", Waiting, created: T0);
        await AddInstance(rig, A, "child", "p", "k1", Waiting, parent: "root", created: T0.AddMinutes(1));
        await AddInstance(rig, A, "grand", "p", "k2", Suspended, parent: "child", created: T0.AddMinutes(2));
        await AddInstance(rig, A, "grand2", "p", "k3", Waiting, parent: "child", created: T0.AddMinutes(3));
        await AddTask(rig, A, "t-1", "grand2", T0);
        await AddTask(rig, A, "t-2", "child", T0);
        await AddTask(rig, A, "t-3", "child", T0, status: 3);

        var view = (await rig.Engine.GetCaseAsync(A, "root"))!;
        var children = await rig.Engine.ListChildrenAsync(A, "child");
        var listed = (await rig.Engine.ListInstancesAsync(new ProcessInstanceQuery(A, RootsOnly: false))).Items.ToDictionary(item => item.InstanceId);

        view.Instance.Should().BeEquivalentTo(listed["root"]);
        view.Instance.OpenTasks.Should().Be(0);
        view.Instance.SuspendedDescendants.Should().Be(1, "the suspended grandchild counts, the instance itself would not");
        view.Descendants.Select(item => (item.InstanceId, item.OpenTasks, item.SuspendedDescendants)).Should().Equal(("child", 1, 1), ("grand", 0, 0), ("grand2", 1, 0));
        view.OpenTasks.Select(task => task.TaskId).Should().BeEquivalentTo("t-1", "t-2");
        children.Select(item => item.InstanceId).Should().Equal("grand", "grand2");
        listed["grand"].SuspendedDescendants.Should().Be(0);
        listed["child"].SuspendedDescendants.Should().Be(1);
        listed["grand"].Status.Should().Be(ProcessStatus.Suspended);
    }

    [Fact]
    public async Task TheWalkStopsAtDepthSixteen()
    {
        await using var rig = await Rig.CreateAsync();
        await AddInstance(rig, A, "n0", "p", "k0", Waiting, created: T0);
        for (var depth = 1; depth <= 18; depth++)
        {
            await AddInstance(rig, A, "n" + depth, "p", "k" + depth, depth == 18 ? Suspended : Waiting, parent: "n" + (depth - 1), created: T0.AddMinutes(depth));
        }

        var view = (await rig.Engine.GetCaseAsync(A, "n0"))!;

        view.Descendants.Select(item => item.InstanceId).Should().Equal(Enumerable.Range(1, 16).Select(depth => "n" + depth));
        view.Instance.SuspendedDescendants.Should().Be(0, "the suspended row is 18 levels down");
        view.Descendants[^1].InstanceId.Should().Be("n16");
        (await rig.Engine.GetCaseAsync(A, "n2"))!.Instance.SuspendedDescendants.Should().Be(1, "from n2 the suspended row is 16 levels down");
    }

    [Fact]
    public async Task ACaseListsTasksTimersAndEventsOfTheWholeTree()
    {
        await using var rig = await Rig.CreateAsync();
        await AddInstance(rig, A, "root", "p", "k0", Waiting, created: T0);
        await AddInstance(rig, A, "child", "p", "k1", Waiting, parent: "root", created: T0.AddMinutes(1));
        await AddInstance(rig, A, "other", "p", "k2", Waiting, created: T0);
        await AddTask(rig, A, "t-b", "child", T0.AddMinutes(5));
        await AddTask(rig, A, "t-a", "child", T0.AddMinutes(5));
        await AddTask(rig, A, "t-0", "root", T0.AddMinutes(9));
        await AddTask(rig, A, "t-x", "other", T0);
        await AddTimer(rig, A, "tm-1", "child", T0.AddHours(3));
        await AddTimer(rig, A, "tm-2", "root", T0.AddHours(2));
        await AddTimer(rig, A, "tm-x", "other", T0.AddHours(1));
        await AddWait(rig, A, "w-1", "child", "z.event", "corr-secret");
        await AddWait(rig, A, "w-2", "root", "a.event");
        await AddWait(rig, A, "w-3", "child", "z.event");
        await AddWait(rig, A, "w-x", "other", "q.event");

        var view = (await rig.Engine.GetCaseAsync(A, "root"))!;

        view.OpenTasks.Select(task => task.TaskId).Should().Equal("t-a", "t-b", "t-0");
        view.NextTimerDue.Should().Be(T0.AddHours(2));
        view.WaitingForEvents.Should().Equal("a.event", "z.event");
        view.OpenTasks.Should().OnlyContain(task => task.ShowJson == ShowText && task.Outcomes.SequenceEqual(GoOnly));
        (await rig.Engine.GetCaseAsync(A, "other"))!.WaitingForEvents.Should().Equal("q.event");
        (await rig.Engine.GetCaseAsync(A, "child"))!.OpenTasks.Should().HaveCount(2);
    }

    [Fact]
    public async Task TheReferenceCaseReportsItsChildrenTasksTimerAndEventsAtEachPoint()
    {
        await using var world = await CaseWorld.CreateAsync();
        var rig = world.Rig;
        rig.Time.SetUtcNow(Ev.At("2027-01-08T23:00:00Z"));
        await world.ActivateLifecycleAsync();
        await world.OpenAsync("t-1", "c-1001");
        await rig.DrainAsync();
        var rootId = (await world.RootAsync("t-1", "c-1001")).InstanceId;

        var friday = (await rig.Engine.GetCaseAsync("t-1", rootId))!;
        friday.Instance.Should().BeEquivalentTo(new { ProcessId = "case.lifecycle", Status = ProcessStatus.Waiting, Stage = "previsit", CurrentStep = "previsit", ParentInstanceId = (string?)null, OpenTasks = 0, SuspendedDescendants = 0 });
        friday.Descendants.Should().ContainSingle().Which.Should().BeEquivalentTo(new { ProcessId = "previsit.outreach", Status = ProcessStatus.Waiting, Stage = "previsit", ParentInstanceId = rootId });
        friday.OpenTasks.Should().BeEmpty();

        rig.Time.SetUtcNow(Ev.At("2027-01-11T14:00:00Z"));
        await rig.DrainAsync();
        var monday = (await rig.Engine.GetCaseAsync("t-1", rootId))!;
        monday.Descendants.Single().CurrentStep.Should().Be("reply");
        monday.WaitingForEvents.Should().Equal("case.reply_received");
        monday.NextTimerDue.Should().NotBeNull();
        monday.OpenTasks.Should().BeEmpty();

        await world.PublishAsync("t-1", "reply-1", "case.reply_received", "c-1001", "{\"text\":null}");
        await rig.DrainAsync();
        var visit = (await rig.Engine.GetCaseAsync("t-1", rootId))!;
        visit.Instance.Stage.Should().Be("visit");
        visit.Descendants.Select(item => (item.ProcessId, item.Status, item.CurrentStep)).Should().Equal(
            ("previsit.outreach", ProcessStatus.Completed, null),
            ("visit.note_wait", ProcessStatus.Waiting, "await_note"));
        visit.NextTimerDue.Should().Be(Ev.At("2027-01-11T15:00:00Z"));
        visit.WaitingForEvents.Should().BeEmpty();
        visit.OpenTasks.Should().BeEmpty();

        rig.Time.Advance(TimeSpan.FromHours(1));
        await rig.DrainAsync();
        var postvisit = (await rig.Engine.GetCaseAsync("t-1", rootId))!;
        postvisit.Instance.Stage.Should().Be("postvisit");
        postvisit.Descendants.Select(item => (item.ProcessId, item.Status, item.OpenTasks)).Should().Equal(
            ("previsit.outreach", ProcessStatus.Completed, 0),
            ("visit.note_wait", ProcessStatus.Completed, 0),
            ("postvisit.coding_review", ProcessStatus.Waiting, 1));
        var task = postvisit.OpenTasks.Should().ContainSingle().Subject;
        task.Queue.Should().Be("physician_review");
        task.Step.Should().Be("physician_review");
        task.Outcomes.Should().Equal("approved", "changed", "rejected");
        task.InstanceId.Should().Be(postvisit.Descendants[2].InstanceId);
        task.ShowJson.Should().Contain("\"suggested_code\":\"C-100\"");
        postvisit.NextTimerDue.Should().BeNull();
        postvisit.WaitingForEvents.Should().BeEmpty();
        (await rig.Engine.ListChildrenAsync("t-1", rootId)).Should().BeEquivalentTo(postvisit.Descendants);
        (await rig.Engine.ListChildrenAsync("t-1", postvisit.Descendants[2].InstanceId)).Should().BeEmpty();
        (await rig.Engine.GetCaseAsync("t-1", "unknown-id")).Should().BeNull();
    }

    [Fact]
    public async Task TheCockpitOfTheGuideShowsTheCaseTheAttentionListAndTheStageCounts()
    {
        await using var world = await CaseWorld.CreateAsync();
        world.MissingFirst = _ => 0;
        world.DraftReads = 0;
        await world.ActivateLifecycleAsync();
        await world.OpenAsync("t-1", "c-1001");
        await world.OpenAsync("t-1", "c-1002");
        await world.Rig.DrainAsync();
        var cockpit = new Cockpit(world.Rig.Engine);

        var view = (await cockpit.ViewAsync("t-1", "c-1001"))!;
        var attention = await cockpit.NeedsAttentionAsync("t-1");
        var stages = await cockpit.OpenCasesPerStageAsync("t-1");

        view.Stage.Should().Be("postvisit");
        view.OpenTasks.Should().Be(1);
        view.NextTimerDue.Should().BeNull();
        view.ChildKeys.Should().HaveCount(3);
        attention.Select(row => row.BusinessKey).Should().BeEquivalentTo("c-1001", "c-1002");
        stages.Should().BeEquivalentTo(new Dictionary<string, int> { ["postvisit"] = 2 });
        (await cockpit.ViewAsync("t-1", "c-9999")).Should().BeNull();
        (await cockpit.NeedsAttentionAsync("t-2")).Should().BeEmpty();
    }

    [Fact]
    public async Task ATaskWithATimeoutShowsBothTheTaskAndTheNextTimer()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("task-timeout"));
        var id = await rig.StartAsync("host.task", "k-1");

        var view = (await rig.Engine.GetCaseAsync(rig.Tenant, id))!;

        view.Instance.Status.Should().Be(ProcessStatus.Waiting);
        view.Instance.OpenTasks.Should().Be(1);
        view.Descendants.Should().BeEmpty();
        view.OpenTasks.Should().ContainSingle().Which.Queue.Should().Be("review_q");
        view.NextTimerDue.Should().Be(Rig.Start.AddHours(1));
        view.WaitingForEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task TheQueriesNeverReadTheStateAndOnlyTheTaskShowUsesTheProtector()
    {
        var protector = new ShowOnlyProtector();
        await using var rig = await Rig.CreateAsync(protector);
        await AddInstance(rig, A, "root", "p", "k0", Suspended, "s", created: T0);
        await AddInstance(rig, A, "child", "p", "k1", Waiting, "s", "root", T0.AddMinutes(1));
        await AddTask(rig, A, "t-1", "child", T0);

        var page = await rig.Engine.ListInstancesAsync(new ProcessInstanceQuery(A, RootsOnly: false, NeedsAttention: true));
        var counts = await rig.Engine.CountInstancesAsync(A, null, false);
        var children = await rig.Engine.ListChildrenAsync(A, "root");
        protector.Unprotected.Should().BeEmpty();
        var view = await rig.Engine.GetCaseAsync(A, "root");

        page.Items.Should().HaveCount(2);
        counts.Should().HaveCount(2);
        children.Should().ContainSingle();
        view!.OpenTasks.Should().ContainSingle().Which.ShowJson.Should().Be(ShowText);
        protector.Unprotected.Should().ContainSingle("the show text of the one open task is the only value a cockpit query decrypts").Which.Should().Be(ShowText);
        (await rig.ScalarAsync<string>("select state from {schema}.process_instance where instance_id = 'root'")).Should().Be(ShowOnlyProtector.Poison);
    }

    [Fact]
    public async Task TheQueriesAreReadOnlyAndTakeNoRowLock()
    {
        await using var rig = await Rig.CreateAsync();
        await AddInstance(rig, A, "root", "p", "k0", Waiting, created: T0);
        await AddInstance(rig, A, "child", "p", "k1", Waiting, parent: "root", created: T0.AddMinutes(1));
        var before = await rig.ScalarAsync<long>("select sum(revision)::bigint from {schema}.process_instance");
        await using var held = await rig.HoldAsync("root", A);

        var page = await rig.Engine.ListInstancesAsync(new ProcessInstanceQuery(A, RootsOnly: false)).WaitAsync(TimeSpan.FromSeconds(10));
        var view = await rig.Engine.GetCaseAsync(A, "root").WaitAsync(TimeSpan.FromSeconds(10));
        var children = await rig.Engine.ListChildrenAsync(A, "root").WaitAsync(TimeSpan.FromSeconds(10));

        page.Items.Should().HaveCount(2);
        view!.Descendants.Should().ContainSingle();
        children.Should().ContainSingle();
        (await rig.ScalarAsync<long>("select sum(revision)::bigint from {schema}.process_instance")).Should().Be(before);
    }

    [Fact]
    public async Task TheMigrationAppliesOnADatabaseAtTheSecondScriptThatHoldsInstances()
    {
        await using var rig = await Rig.CreateAsync();
        await rig.ExecuteAsync("drop index {schema}.ix_process_instance_created; drop index {schema}.ix_process_instance_status; drop index {schema}.ix_process_instance_purge; drop index {schema}.ix_process_event_received; drop index {schema}.ix_process_inbox_instance; delete from {schema}.process_schema_version where version >= 3");
        await AddInstance(rig, A, "root", "p", "k0", Waiting, "s", created: T0);
        await AddInstance(rig, A, "child", "p", "k1", Suspended, "s", "root", T0.AddMinutes(1));
        (await rig.ColumnAsync<int>("select version from {schema}.process_schema_version order by version")).Should().Equal(1, 2);

        await rig.Provider.GetRequiredService<IProcessStoreMigrator>().MigrateAsync();

        (await rig.ColumnAsync<int>("select version from {schema}.process_schema_version order by version")).Should().Equal(1, 2, 3, 4);
        (await rig.ColumnAsync<string>("select indexname from pg_indexes where schemaname = @s and tablename = 'process_instance' and indexname in ('ix_process_instance_created', 'ix_process_instance_status') order by indexname", ("s", rig.Schema)))
            .Should().Equal("ix_process_instance_created", "ix_process_instance_status");
        (await rig.ColumnAsync<string>("select indexdef from pg_indexes where schemaname = @s and indexname = 'ix_process_instance_created'", ("s", rig.Schema)))
            .Single().Should().Contain("(tenant_id, created_at DESC, instance_id COLLATE \"C\" DESC)");
        (await rig.CountAsync("process_instance")).Should().Be(2);
        (await rig.Engine.GetCaseAsync(A, "root"))!.Instance.SuspendedDescendants.Should().Be(1);
    }

    private static readonly string[] GoOnly = ["go"];
    private static readonly int[] PageSizes = [1, 2, 3, 4, 7, 10, 11];

    private const string ShowText = "{\"k\":1}";

    private static Task AddInstance(Rig rig, string tenant, string id, string process, string key, int status, string? stage = null, string? parent = null, DateTimeOffset? created = null) =>
        rig.ExecuteAsync(
            "insert into {schema}.process_instance (tenant_id, instance_id, process_id, process_version, process_hash, business_key, status, stage, current_step, result, problem_code, state, revision, parent_instance_id, parent_child_key, created_at, updated_at) "
            + "values (@t, @i, @p, 1, 'h', @k, @s, @g, null, null, null, @state, 1, @par, null, @c, @c)",
            ("t", tenant),
            ("i", id),
            ("p", process),
            ("k", key),
            ("s", (short)status),
            ("g", stage),
            ("state", ShowOnlyProtector.Poison),
            ("par", parent),
            ("c", created ?? T0));

    private static Task AddTask(Rig rig, string tenant, string id, string instance, DateTimeOffset createdAt, int status = 1) =>
        rig.ExecuteAsync(
            "insert into {schema}.process_task (tenant_id, task_id, instance_id, step, queue, outcomes, show, status, created_at) values (@t, @id, @i, 'step', 'queue', '[\"go\"]', @show, @st, @c)",
            ("t", tenant),
            ("id", id),
            ("i", instance),
            ("show", ShowText),
            ("st", (short)status),
            ("c", createdAt));

    private static Task AddTimer(Rig rig, string tenant, string id, string instance, DateTimeOffset dueAt) =>
        rig.ExecuteAsync("insert into {schema}.process_timer (tenant_id, timer_id, instance_id, due_at) values (@t, @id, @i, @d)", ("t", tenant), ("id", id), ("i", instance), ("d", dueAt));

    private static Task AddWait(Rig rig, string tenant, string id, string instance, string name, string? correlation = null) =>
        rig.ExecuteAsync("insert into {schema}.process_wait (tenant_id, wait_id, instance_id, event, correlation) values (@t, @id, @i, @e, @c)", ("t", tenant), ("id", id), ("i", instance), ("e", name), ("c", correlation));

    /// <summary>A protector that refuses to protect anything and refuses to read anything but the show text of the test tasks; it records what it was asked to read.</summary>
    private sealed class ShowOnlyProtector : IPayloadProtector
    {
        internal const string Poison = "POISON-STATE";

        private readonly List<string> unprotected = [];

        internal IReadOnlyList<string> Unprotected => [.. unprotected];

        public string Protect(string tenantId, string plaintext) => throw new InvalidOperationException("A cockpit query must not protect anything.");

        public string Unprotect(string tenantId, string protectedText)
        {
            unprotected.Add(protectedText);
            return protectedText == ShowText ? protectedText : throw new InvalidOperationException("A cockpit query must not read this value.");
        }
    }
}

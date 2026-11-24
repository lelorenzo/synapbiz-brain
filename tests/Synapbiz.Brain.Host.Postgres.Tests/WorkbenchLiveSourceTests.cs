using System.Text.Json;
using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Flows;
using Synapbiz.Brain.Workbench.Postgres;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>
/// The live source of the process workbench over the real host: the reference lifecycle driven through the engine, then read through
/// <see cref="PostgresFlowLiveSource"/>. A case reference is the instance id, never the business key; the history carries no fact.
/// </summary>
public sealed class WorkbenchLiveSourceTests
{
    private const string Tenant = "t-1";
    private const string Business = "c-1001";

    private static readonly BrainProcess[] Documents = ReferenceFlows.All();

    private static BrainProcess Doc(string id) => Documents.Single(process => process.Id == id);

    private static async Task<CaseWorld> WorldAtPhysicianReviewAsync(params string[] caseRefs)
    {
        var world = await CaseWorld.CreateAsync();
        world.MissingFirst = _ => 0;
        world.DraftReads = 0;
        await world.ActivateLifecycleAsync();
        foreach (var caseRef in caseRefs)
        {
            (await world.OpenAsync(Tenant, caseRef)).Instances.Should().Be(1);
        }

        await world.Rig.DrainAsync();
        return world;
    }

    [Fact]
    public async Task An_empty_database_has_no_counts_no_cases_and_no_case()
    {
        await using var rig = await Rig.CreateAsync();
        var source = new PostgresFlowLiveSource(rig.Engine, Tenant);

        var none = await source.GetCountsAsync();
        none.Rows.Should().BeEmpty();
        none.IsPartial.Should().BeFalse();
        var none2 = await source.ListCasesAsync(new FlowLiveCaseQuery("case.lifecycle"));
        none2.Cases.Should().BeEmpty();
        none2.Next.Should().BeNull();
        (await source.GetCaseAsync("nope")).Should().BeNull();
    }

    [Fact]
    public async Task The_counts_are_the_open_instances_per_process_and_step_and_those_with_a_task_for_a_person_wait()
    {
        await using var world = await WorldAtPhysicianReviewAsync(Business);
        var source = new PostgresFlowLiveSource(world.Rig.Engine, Tenant);

        var counts = await source.GetCountsAsync();

        counts.IsPartial.Should().BeFalse();
        var review = counts.Rows.Single(row => row.ProcessId == "postvisit.coding_review");
        review.Open.Should().Be(1);
        review.Waiting.Should().Be(1, "the physician review is a task for a person");
        review.Step.Should().Be("physician_review");
        var root = counts.Rows.Single(row => row.ProcessId == "case.lifecycle");
        root.Open.Should().Be(1);
        root.Waiting.Should().Be(0, "the root waits for a child, not for a person");
        counts.Rows.Select(row => row.ProcessId).Should().BeEquivalentTo(["case.lifecycle", "postvisit.coding_review"], "the children that finished are not open cases");

        var live = ProcessMapLive.From(Documents, counts);
        live.Process("postvisit.coding_review").Should().Be(new LiveCount(1, 1));
        live.Phase("case.lifecycle", "postvisit").Should().Be(new LiveCount(1, 0));
        live.Unplaced.IsZero.Should().BeTrue();
        live.Total.Should().Be(new LiveCount(2, 1));
    }

    [Fact]
    public async Task A_partial_count_says_so_when_it_stops_at_its_limit()
    {
        await using var world = await WorldAtPhysicianReviewAsync("c-1", "c-2", "c-3");
        var all = await new PostgresFlowLiveSource(world.Rig.Engine, Tenant).GetCountsAsync();
        var capped = await new PostgresFlowLiveSource(world.Rig.Engine, Tenant, 2, 2).GetCountsAsync();

        all.IsPartial.Should().BeFalse();
        all.Rows.Sum(row => row.Open).Should().Be(6);
        capped.IsPartial.Should().BeTrue();
        capped.Rows.Sum(row => row.Open).Should().BeLessThan(6).And.BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task A_page_of_cases_holds_opaque_references_and_pages_through_the_cursor()
    {
        await using var world = await WorldAtPhysicianReviewAsync("c-1", "c-2", "c-3");
        var source = new PostgresFlowLiveSource(world.Rig.Engine, Tenant);

        var first = await source.ListCasesAsync(new FlowLiveCaseQuery("postvisit.coding_review", "physician_review", 2));
        var second = await source.ListCasesAsync(new FlowLiveCaseQuery("postvisit.coding_review", "physician_review", 2, first.Next));

        first.Cases.Should().HaveCount(2);
        first.Next.Should().NotBeNull();
        second.Cases.Should().ContainSingle();
        second.Next.Should().BeNull();
        var refs = first.Cases.Concat(second.Cases).Select(line => line.CaseRef).ToList();
        refs.Should().OnlyHaveUniqueItems().And.HaveCount(3);
        refs.Should().OnlyContain(reference => reference.Length == 32 && reference.All(char.IsAsciiHexDigit), "an instance id the host generated");
        first.Cases.Concat(second.Cases).Should().OnlyContain(line => line.WaitsForPerson && line.Step == "physician_review");
        JsonSerializer.Serialize(first).Should().NotContain("c-1").And.NotContain("c-2");
    }

    [Fact]
    public async Task A_step_filter_keeps_only_the_cases_on_that_step_and_a_step_nobody_is_on_gives_an_empty_page()
    {
        await using var world = await WorldAtPhysicianReviewAsync(Business);
        var source = new PostgresFlowLiveSource(world.Rig.Engine, Tenant);

        var everyone = await source.ListCasesAsync(new FlowLiveCaseQuery("case.lifecycle"));
        var wrong = await source.ListCasesAsync(new FlowLiveCaseQuery("case.lifecycle", "no.such.step"));
        var other = await source.ListCasesAsync(new FlowLiveCaseQuery("postvisit.coding_review", "suggest"));

        everyone.Cases.Should().ContainSingle().Which.WaitsForPerson.Should().BeFalse();
        wrong.Cases.Should().BeEmpty();
        wrong.Next.Should().BeNull();
        other.Cases.Should().BeEmpty();
    }

    [Fact]
    public async Task The_history_of_a_case_replays_over_its_document_and_carries_no_fact_and_no_business_key()
    {
        await using var world = await WorldAtPhysicianReviewAsync(Business);
        var source = new PostgresFlowLiveSource(world.Rig.Engine, Tenant);
        var line = (await source.ListCasesAsync(new FlowLiveCaseQuery("postvisit.coding_review"))).Cases.Single();

        var live = (await source.GetCaseAsync(line.CaseRef))!;
        var path = LiveCasePath.From(Doc("postvisit.coding_review"), live, WorkbenchLocale.En, Documents);

        live.CaseRef.Should().Be(line.CaseRef);
        live.ProcessVersion.Should().Be(ProcessOutline.From(Doc("postvisit.coding_review")).Version);
        live.Status.Should().Be(ProcessStatus.Waiting);
        live.Wait.Queues.Should().Equal("physician_review");
        live.History.Should().NotBeEmpty().And.Contain(entry => entry.Kind == ProcessHistoryKind.Decided);
        var text = JsonSerializer.Serialize(live);
        text.Should().NotContain("facts").And.NotContain("factsHash").And.NotContain(Business);
        path.IsExplained.Should().BeTrue(path.Reason);
        path.CurrentStep.Should().Be("physician_review");
        path.Waiting.Should().Be("Waits for a person in the queue Physician review.");
        path.Side!.Why.Should().NotBeNullOrWhiteSpace();
        path.Steps.Select(step => step.Id).Should().Equal(live.History.Where(entry => entry.Kind == ProcessHistoryKind.StepEntered).Select(entry => entry.Step!).Distinct());
    }

    [Fact]
    public async Task The_case_that_waits_for_a_child_does_not_claim_the_childs_task_and_names_the_child()
    {
        await using var world = await WorldAtPhysicianReviewAsync(Business);
        var source = new PostgresFlowLiveSource(world.Rig.Engine, Tenant);
        var root = (await source.ListCasesAsync(new FlowLiveCaseQuery("case.lifecycle"))).Cases.Single();

        var live = (await source.GetCaseAsync(root.CaseRef))!;
        var path = LiveCasePath.From(Doc("case.lifecycle"), live, WorkbenchLocale.Es, Documents);

        live.Wait.Queues.Should().BeEmpty("the task belongs to the child instance");
        live.Wait.Until.Should().BeNull();
        live.Wait.Events.Should().BeEmpty();
        path.IsExplained.Should().BeTrue(path.Reason);
        path.Waiting.Should().StartWith("Espera a que termine el proceso");
        path.Side!.Version.Should().Be(live.ProcessVersion);
    }

    [Fact]
    public async Task A_finished_case_is_not_counted_or_listed_but_can_still_be_read_and_says_how_it_ended()
    {
        await using var world = await WorldAtPhysicianReviewAsync(Business);
        var source = new PostgresFlowLiveSource(world.Rig.Engine, Tenant);
        var review = await world.TaskAsync(Tenant, "physician_review");
        var child = (await source.ListCasesAsync(new FlowLiveCaseQuery("postvisit.coding_review"))).Cases.Single();
        await world.CompleteAsync(review, "rejected");

        var counts = await source.GetCountsAsync();
        var page = await source.ListCasesAsync(new FlowLiveCaseQuery("postvisit.coding_review"));
        var live = (await source.GetCaseAsync(child.CaseRef))!;
        var path = LiveCasePath.From(Doc("postvisit.coding_review"), live, WorkbenchLocale.En, Documents);

        counts.Rows.Should().BeEmpty();
        page.Cases.Should().BeEmpty();
        live.Status.Should().Be(ProcessStatus.Completed);
        path.IsExplained.Should().BeTrue(path.Reason);
        path.CurrentStep.Should().BeNull();
        path.Waiting.Should().StartWith("The case ended with the result");
    }

    [Fact]
    public async Task A_source_of_one_tenant_never_sees_the_cases_of_another()
    {
        await using var world = await WorldAtPhysicianReviewAsync(Business);
        var mine = new PostgresFlowLiveSource(world.Rig.Engine, Tenant);
        var theirs = new PostgresFlowLiveSource(world.Rig.Engine, "t-other");
        var line = (await mine.ListCasesAsync(new FlowLiveCaseQuery("postvisit.coding_review"))).Cases.Single();

        (await theirs.GetCountsAsync()).Rows.Should().BeEmpty();
        (await theirs.ListCasesAsync(new FlowLiveCaseQuery("postvisit.coding_review"))).Cases.Should().BeEmpty();
        (await theirs.GetCaseAsync(line.CaseRef)).Should().BeNull();
        (await mine.GetCaseAsync(line.CaseRef)).Should().NotBeNull();
    }

    [Fact]
    public async Task The_arguments_are_checked_and_a_cancelled_call_stops()
    {
        await using var rig = await Rig.CreateAsync();
        var source = new PostgresFlowLiveSource(rig.Engine, Tenant);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        FluentActions.Invoking(() => new PostgresFlowLiveSource(null!, Tenant)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new PostgresFlowLiveSource(rig.Engine, string.Empty)).Should().Throw<ArgumentException>();
        await FluentActions.Awaiting(() => source.ListCasesAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => source.GetCaseAsync(string.Empty)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => source.ListCasesAsync(new FlowLiveCaseQuery("p", Limit: 0))).Should().ThrowAsync<ArgumentOutOfRangeException>();
        await FluentActions.Awaiting(() => source.GetCountsAsync(cancelled.Token)).Should().ThrowAsync<OperationCanceledException>();
    }
}

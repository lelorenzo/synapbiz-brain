using Bunit;
using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;
using Synapbiz.Brain.Workbench.Tests;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>
/// The live view after verification: the list asks the source for a single step, says "none among the cases read so far" while more
/// remain, a stale answer never replaces a newer one (with a source that ignores its cancellation token and answers out of order), and
/// one case has a Refresh control.
/// </summary>
public sealed class FlowLiveAmendmentTests
{
    private static readonly DateTimeOffset Since = new(2026, 3, 2, 15, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<WorkbenchFlow> MapFlows() =>
    [.. MapDocs.All.Select(process => new WorkbenchFlow { Key = process.Id, Group = "Synthetic", Process = process })];

    private static FlowHarness Open(IFlowLiveSource source, IReadOnlyList<WorkbenchFlow>? flows = null, WorkbenchLocale locale = WorkbenchLocale.En)
    {
        var harness = new FlowHarness();
        harness.Session.View = FlowWorkbenchView.Live;
        harness.Render(flows ?? FlowHarness.Reference(), locale, view: null, more: parameters => parameters.Add(component => component.LiveSource, source));
        return harness;
    }

    private static string Text(AngleSharp.Dom.IElement element) => FlowHarness.Normalise(element.TextContent);

    private static FlowLiveCaseLine Line(string reference, string? step, long version = 1) => new(reference, version, step, false, Since);

    /// <summary>A source of case lines in one process that pages by position and honours the step of a query when it has one.</summary>
    private sealed class PagedSource(string processId, List<FlowLiveCaseLine> lines) : IFlowLiveSource
    {
        internal List<FlowLiveCaseQuery> Queries { get; } = [];

        public Task<FlowLiveCounts> GetCountsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new FlowLiveCounts([.. lines.GroupBy(line => line.Step).Select(group => new FlowLiveCount(processId, group.Key, group.Count(), 0))]));

        public Task<FlowLiveCasePage> ListCasesAsync(FlowLiveCaseQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            var all = lines.Where(line => query.Step is null || line.Step == query.Step).ToList();
            var from = query.After is null ? 0 : int.Parse(query.After, System.Globalization.CultureInfo.InvariantCulture);
            var size = Math.Min(query.Limit, 25);
            var next = from + size < all.Count ? (from + size).ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
            return Task.FromResult(new FlowLiveCasePage([.. all.Skip(from).Take(size)], next));
        }

        public Task<FlowLiveCase?> GetCaseAsync(string caseRef, CancellationToken cancellationToken = default) => Task.FromResult<FlowLiveCase?>(null);
    }

    [Fact]
    public void A_phase_of_a_single_step_is_asked_of_the_source_by_that_step_so_that_other_steps_do_not_fill_the_pages()
    {
        // map.front has one step with no stage ("after"); 300 older cases sit on another step.
        var lines = new List<FlowLiveCaseLine>();
        lines.AddRange(Enumerable.Range(1, 300).Select(number => Line("other-" + number, "triage")));
        lines.AddRange(Enumerable.Range(1, 3).Select(number => Line("after-" + number, "after")));
        var source = new PagedSource("map.front", lines);
        using var harness = Open(source, MapFlows());

        harness.Session.LiveScopeProcessId = "map.front";
        harness.Cut.Render();
        harness.Cut.WaitForAssertion(() => harness.FindAll("ul.wb-caselist li").Should().HaveCount(25)); // the whole process is listed first
        harness.Session.LiveScopeStageId = string.Empty;
        harness.Cut.Render();

        harness.Cut.WaitForAssertion(() => harness.FindAll("ul.wb-caselist li .wb-caseref").Select(Text).Should().Equal("Case after-1", "Case after-2", "Case after-3")); // the steps with no stage are another list than the whole process
        source.Queries.Skip(1).Should().NotBeEmpty().And.OnlyContain(query => query.Step == "after", "the first query was the whole process");
        harness.FindAll("section.wb-side p.wb-sub").Select(Text).Should().NotContain("No open cases here.");
    }

    [Theory]
    [InlineData("en", "None among the cases read so far.", "Show more cases")]
    [InlineData("es", "Ninguno entre los casos leídos hasta ahora.", "Mostrar más casos")]
    public void When_nothing_matched_but_more_cases_remain_the_list_says_so_beside_show_more(string language, string none, string more)
    {
        // The phase "intake" has several steps, so the filter is the component's own; 300 older cases are on the other phase.
        var lines = new List<FlowLiveCaseLine>();
        lines.AddRange(Enumerable.Range(1, 300).Select(number => Line("later-" + number, "do_serve")));
        lines.Add(Line("here-1", "triage"));
        var source = new PagedSource("map.front", lines);
        var locale = language == "es" ? WorkbenchLocale.Es : WorkbenchLocale.En;
        using var harness = Open(source, MapFlows(), locale);

        harness.Session.LiveScopeProcessId = "map.front";
        harness.Session.LiveScopeStageId = "intake";
        harness.Cut.Render();

        harness.Cut.WaitForAssertion(() => harness.FindAll("section.wb-side p.wb-sub[role=status]").Select(Text).Should().Equal(none));
        harness.FindAll("ul.wb-caselist li").Should().BeEmpty();
        Text(harness.Find("section.wb-side .wb-body > button")).Should().Be(more);
        source.Queries.Should().OnlyContain(query => query.Step == null);

        harness.Find("section.wb-side .wb-body > button").Click();

        harness.Cut.WaitForAssertion(() => harness.FindAll("ul.wb-caselist li .wb-caseref").Select(Text).Should().Equal((language == "es" ? "Caso " : "Case ") + "here-1"));
        harness.FindAll("section.wb-side p.wb-sub[role=status]").Should().BeEmpty();
        harness.FindAll("section.wb-side .wb-body > button").Should().BeEmpty("the last page was read");
    }

    [Fact]
    public void When_nothing_matched_and_nothing_remains_the_list_still_says_there_are_no_open_cases_here()
    {
        var source = new PagedSource("map.front", [Line("later-1", "do_serve")]);
        using var harness = Open(source, MapFlows());

        harness.Session.LiveScopeProcessId = "map.front";
        harness.Session.LiveScopeStageId = "intake";
        harness.Cut.Render();

        harness.Cut.WaitForAssertion(() => harness.FindAll("section.wb-side p.wb-sub[role=status]").Select(Text).Should().Equal("No open cases here."));
    }

    /// <summary>A source that ignores its cancellation token and answers each call when the test says so, in any order.</summary>
    private sealed class ManualSource : IFlowLiveSource
    {
        internal List<TaskCompletionSource<FlowLiveCounts>> Counts { get; } = [];

        internal List<TaskCompletionSource<FlowLiveCasePage>> Lists { get; } = [];

        internal List<TaskCompletionSource<FlowLiveCase?>> Cases { get; } = [];

        public Task<FlowLiveCounts> GetCountsAsync(CancellationToken cancellationToken = default) => Next(Counts);

        public Task<FlowLiveCasePage> ListCasesAsync(FlowLiveCaseQuery query, CancellationToken cancellationToken = default) => Next(Lists);

        public Task<FlowLiveCase?> GetCaseAsync(string caseRef, CancellationToken cancellationToken = default) => Next(Cases);

        private static Task<T> Next<T>(List<TaskCompletionSource<T>> calls)
        {
            var call = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            calls.Add(call);
            return call.Task;
        }
    }

    [Fact]
    public async Task A_stale_answer_to_the_counts_never_replaces_a_newer_one_even_when_the_source_ignores_cancellation()
    {
        var source = new ManualSource();
        using var harness = Open(source);
        source.Counts.Should().HaveCount(1);
        var second = harness.Cut.InvokeAsync(() => harness.Cut.Instance.Refresh());
        source.Counts.Should().HaveCount(2);

        source.Counts[1].SetResult(new FlowLiveCounts([new FlowLiveCount("case.lifecycle", null, 3, 0)]));
        await second;
        source.Counts[0].SetResult(new FlowLiveCounts([new FlowLiveCount("case.lifecycle", null, 7, 0)]));
        await Task.Delay(50);
        harness.Cut.Render();

        Text(harness.Find(".wb-live-status")).Should().Be("3 open cases in all");
    }

    [Fact]
    public async Task A_stale_page_of_the_list_never_replaces_a_newer_one_even_when_the_source_ignores_cancellation()
    {
        var source = new ManualSource();
        using var harness = Open(source);
        source.Counts[0].SetResult(new FlowLiveCounts([new FlowLiveCount("case.lifecycle", null, 2, 0)]));
        harness.Cut.WaitForAssertion(() => harness.FindAll(".wb-live-btn").Should().NotBeEmpty());

        harness.Session.LiveScopeProcessId = "case.lifecycle";
        harness.Cut.Render();
        harness.Cut.WaitForAssertion(() => source.Lists.Should().HaveCount(1));
        harness.Session.LiveScopeStageId = "previsit";
        harness.Cut.Render();
        harness.Cut.WaitForAssertion(() => source.Lists.Should().HaveCount(2));

        source.Lists[1].SetResult(new FlowLiveCasePage([Line("case-new", "zone_check")], null));
        harness.Cut.WaitForAssertion(() => harness.FindAll("ul.wb-caselist li .wb-caseref").Select(Text).Should().Equal("Case case-new"));
        source.Lists[0].SetResult(new FlowLiveCasePage([Line("case-old", "previsit")], null));
        await Task.Delay(50);
        harness.Cut.Render();

        harness.FindAll("ul.wb-caselist li .wb-caseref").Select(Text).Should().Equal("Case case-new");
    }

    [Fact]
    public async Task A_stale_answer_for_one_case_never_replaces_a_newer_one_even_when_the_source_ignores_cancellation()
    {
        var scripted = ScriptedLiveSource.Reference();
        var first = scripted.Cases.First(item => item.ProcessId == "case.lifecycle");
        var second = scripted.Cases.First(item => item.ProcessId == "collection.followup");
        var titles = ProcessMap.From(FlowDocs.Reference.Select(item => item.Process), WorkbenchLocale.En);
        var source = new ManualSource();
        using var harness = Open(source);
        source.Counts[0].SetResult(FlowLiveCounts.None);

        harness.Session.LiveCaseRef = "c-first";
        harness.Cut.Render();
        harness.Cut.WaitForAssertion(() => source.Cases.Should().HaveCount(1));
        harness.Session.LiveCaseRef = "c-second";
        harness.Cut.Render();
        harness.Cut.WaitForAssertion(() => source.Cases.Should().HaveCount(2));

        source.Cases[1].SetResult(second with { CaseRef = "c-second" });
        harness.Cut.WaitForAssertion(() => Text(harness.Find("section[aria-labelledby$=-case] .wb-body > p.wb-meta")).Should().StartWith(titles.Process("collection.followup")!.Title));
        source.Cases[0].SetResult(first with { CaseRef = "c-first" });
        await Task.Delay(50);
        harness.Cut.Render();

        Text(harness.Find("section[aria-labelledby$=-case] .wb-body > p.wb-meta")).Should().StartWith(titles.Process("collection.followup")!.Title);
    }

    [Theory]
    [InlineData("en", "Refresh")]
    [InlineData("es", "Actualizar")]
    public void One_case_has_a_refresh_control_that_reads_the_counts_and_the_case_again(string language, string word)
    {
        var locale = language == "es" ? WorkbenchLocale.Es : WorkbenchLocale.En;
        var source = ScriptedLiveSource.Reference();
        using var harness = Open(source, locale: locale);
        var live = source.Cases[0];
        harness.Session.LiveCaseRef = live.CaseRef;
        harness.Cut.Render();
        harness.Cut.WaitForAssertion(() => harness.FindAll("g.wb-node-seen").Should().NotBeEmpty());
        var counts = source.CountCalls;
        var cases = source.CaseCalls;

        var button = harness.FindAll("section[aria-labelledby$=-case] .wb-ph button").Single(item => Text(item) == word);
        button.HasAttribute("disabled").Should().BeFalse();
        button.Click();

        source.CountCalls.Should().Be(counts + 1);
        source.CaseCalls.Should().Be(cases + 1);
    }
}

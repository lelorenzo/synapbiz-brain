using System.Text.Json.Nodes;
using FluentAssertions;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Flows;
using static Synapbiz.Brain.Authoring.Tests.Fixtures;

namespace Synapbiz.Brain.Authoring.Tests;

/// <summary>The checks beyond the loader: each has a fixture that fails it, a sentence in both languages, and an excuse where the specification allows one.</summary>
public sealed class ReadinessTests
{
    private const string NW = "visit.note_wait";
    private const string PV = "previsit.outreach";
    private const string PC = "postvisit.coding_review";

    private static ReadinessReport Check(string id, string? patch = null, bool withoutTests = false, IReadOnlyList<KnownGap>? gaps = null, AuthoringCatalog? catalog = null)
    {
        var text = ReferenceDocs.Changed(id, patch, withoutTests);
        var loaded = ReferenceDocs.TryLoad(id, text);
        var document = AuthoringJson.TryObject(text);
        var used = catalog ?? AuthoringCatalog.Unrestricted;
        var report = loaded.Process is null ? null : FlowScenarioReport.Of(loaded.Process, WorkbenchLocale.En);
        return ReadinessChecks.Run(document, loaded.Errors, loaded.Process, report, gaps ?? [], used, document is null ? [] : CatalogCheck.Draft(used, document));
    }

    private static IReadOnlyList<ReadinessFinding> Findings(ReadinessReport report, string code) => report.Checks.Single(check => check.Code == code).Findings;

    [Fact]
    public void A_reference_process_with_its_tests_and_labels_is_ready()
    {
        var report = Check(NW, catalog: NoteCatalog());
        report.Ready.Should().BeTrue(string.Join("; ", report.Findings.Select(finding => finding.TextEn)));
        report.Checks.Select(check => check.Code).Should().Equal(ReadinessChecks.Codes);
    }

    [Fact]
    public void The_loader_errors_fail_the_first_check()
    {
        var report = Check(NW, """[{"op":"replace","path":"/body/steps/await_note/onExpired","value":"nowhere"}]""");
        Findings(report, ReadinessChecks.LoaderErrors).Should().NotBeEmpty();
        report.Ready.Should().BeFalse();
    }

    [Fact]
    public void A_scenario_that_fails_fails_its_check_and_names_the_test()
    {
        var report = Check(NW, """[{"op":"replace","path":"/body/steps/chase_note/on/signed","value":"cancelled"}]""");
        var findings = Findings(report, ReadinessChecks.ScenarioFailing);
        findings.Should().NotBeEmpty();
        findings[0].TextEn.Should().Contain("front desk learns");
    }

    [Fact]
    public void A_step_without_a_name_in_one_language_fails_the_label_check()
    {
        var report = Check(NW, """[{"op":"remove","path":"/meta/display/es/steps/confirm_note"}]""");
        var findings = Findings(report, ReadinessChecks.LabelMissing);
        findings.Should().ContainSingle();
        findings[0].Step.Should().Be("confirm_note");
        findings[0].TextEn.Should().Contain("Spanish");
        findings[0].TextEs.Should().Contain("español");
    }

    [Fact]
    public void A_route_no_test_takes_fails_unless_the_author_excuses_it_with_a_reason()
    {
        var report = Check(NW, withoutTests: true);
        var untested = Findings(report, ReadinessChecks.RouteUntested);
        untested.Should().NotBeEmpty();
        var first = untested[0];

        var excused = Check(NW, withoutTests: true, gaps: [.. untested.Select(finding => new KnownGap(KnownGap.RouteUntested, finding.Step!, finding.Route, "Checked by hand with the front desk."))]);
        Findings(excused, ReadinessChecks.RouteUntested).Should().BeEmpty();
        var partly = Check(NW, withoutTests: true, gaps: [new KnownGap(KnownGap.RouteUntested, first.Step!, first.Route, "Only this one.")]);
        Findings(partly, ReadinessChecks.RouteUntested).Should().HaveCount(untested.Count - 1);
    }

    [Fact]
    public void A_step_that_contacts_a_person_must_be_at_most_once_behind_a_window_and_have_routes_for_failure_and_unknown()
    {
        Findings(Check(PV), ReadinessChecks.ContactUnsafe).Should().BeEmpty("the reference process does it right");

        var report = Check(PV, """
            [{"op":"replace","path":"/body/steps/send/delivery","value":"atLeastOnce"},
             {"op":"remove","path":"/body/steps/send/onUnknown"}]
            """);
        var findings = Findings(report, ReadinessChecks.ContactUnsafe);
        findings.Should().NotBeEmpty();
        findings.Should().OnlyContain(finding => finding.Step == "send");
        findings.Select(finding => finding.TextEn).Should().Contain(text => text.Contains("at most once", StringComparison.Ordinal) || text.Contains("once", StringComparison.Ordinal));
    }

    [Fact]
    public void A_port_the_catalog_says_contacts_a_person_counts_even_when_the_step_does_not_say_so()
    {
        var catalog = new AuthoringCatalog
        {
            Ports =
            [
                new CatalogPort("messaging.reminder", "action", "either", true, null, null, null),
                new CatalogPort("records.note_status", "read", "either", false, null, null, null),
            ],
        };
        var report = Check(PV, """
            [{"op":"replace","path":"/body/steps/send/contact","value":false},
             {"op":"replace","path":"/body/steps/send/delivery","value":"atLeastOnce"},
             {"op":"remove","path":"/body/steps/send/onUnknown"}]
            """, catalog: catalog);
        Findings(report, ReadinessChecks.ContactUnsafe).Should().NotBeEmpty(string.Join("; ", Findings(report, ReadinessChecks.LoaderErrors).Select(finding => finding.TextEn)));
    }

    [Fact]
    public void An_ai_step_needs_every_failure_route_to_reach_an_end_without_ai()
    {
        Findings(Check(PC), ReadinessChecks.AiWithoutFallback).Should().BeEmpty("the reference process has a way without AI");

        var report = Check(PC, """[{"op":"remove","path":"/body/steps/suggest/onError"}]""");
        var findings = Findings(report, ReadinessChecks.AiWithoutFallback);
        findings.Should().ContainSingle();
        findings[0].Step.Should().Be("suggest");
        findings[0].Route.Should().Be("onError");
    }

    [Fact]
    public void A_wait_without_a_timeout_fails_unless_the_author_says_why()
    {
        var patch = """
            [{"op":"remove","path":"/body/steps/reply/timeout"},
             {"op":"remove","path":"/body/steps/reply/onTimeout"}]
            """;
        var report = Check(PV, patch);
        var findings = Findings(report, ReadinessChecks.WaitWithoutTimeout);
        findings.Should().ContainSingle();
        findings[0].Step.Should().Be("reply");

        Findings(Check(PV, patch, gaps: [new KnownGap(KnownGap.WaitWithoutTimeout, "reply", null, "The patient may answer weeks later and the case stays open on purpose.")]), ReadinessChecks.WaitWithoutTimeout).Should().BeEmpty();
    }

    [Fact]
    public void Everything_the_process_names_must_exist_in_the_catalog()
    {
        var catalog = new AuthoringCatalog { Ports = [], Events = [], Queues = [], Stages = [] };
        var report = Check(NW, catalog: catalog);
        var findings = Findings(report, ReadinessChecks.OutsideCatalog);
        findings.Should().NotBeEmpty();
        findings.Select(finding => finding.TextEn).Should().Contain(text => text.Contains("not available here", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_check_has_a_requirement_sentence_in_both_languages_and_every_finding_has_both()
    {
        foreach (var code in ReadinessChecks.Codes)
        {
            ReadinessChecks.Requirement(code, WorkbenchLocale.En).Should().NotBeNullOrWhiteSpace(code);
            ReadinessChecks.Requirement(code, WorkbenchLocale.Es).Should().NotBeNullOrWhiteSpace(code);
            ReadinessChecks.Requirement(code, WorkbenchLocale.En).Should().NotBe(ReadinessChecks.Requirement(code, WorkbenchLocale.Es));
        }

        var report = Check(NW, """[{"op":"remove","path":"/meta/display/es/steps/confirm_note"}]""", withoutTests: true, catalog: new AuthoringCatalog { Ports = [], Queues = [] });
        report.Findings.Should().NotBeEmpty();
        foreach (var finding in report.Findings)
        {
            finding.TextEn.Should().NotBeNullOrWhiteSpace();
            finding.TextEs.Should().NotBeNullOrWhiteSpace();
            finding.TextEn.Should().NotBe(finding.TextEs);
            finding.TextEn.Should().NotContain("{").And.NotContain("}");
            finding.TextEs.Should().NotContain("{").And.NotContain("}");
            finding.Text(WorkbenchLocale.Es).Should().Be(finding.TextEs);
        }
    }

    [Fact]
    public void The_codes_are_the_seven_checks_of_the_specification_plus_the_loader()
    {
        ReadinessChecks.Codes.Should().HaveCount(8);
        ReadinessChecks.Codes.Should().OnlyHaveUniqueItems();
        ReadinessChecks.Codes.Should().OnlyContain(code => code.StartsWith("authoring.ready.", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_catalog_problem_has_a_finding_text_in_both_languages()
    {
        var samples = new[]
        {
            new CatalogProblem(CatalogCheck.PortUnavailable, "/body/steps/s/port", "port", "p", ["q"], "s", null),
            new CatalogProblem(CatalogCheck.PortKind, "/body/steps/s/port", "port", "p", ["q"], "s", "read"),
            new CatalogProblem(CatalogCheck.DeliveryUnsupported, "/body/steps/s/delivery", "port", "p", [], "s", "atMostOnce"),
            new CatalogProblem(CatalogCheck.EventUnavailable, "/body/accepts/0", "event", "e", [], null, null),
            new CatalogProblem(CatalogCheck.QueueUnavailable, "/body/steps/s/queue", "queue", "q", ["a"], "s", null),
            new CatalogProblem(CatalogCheck.StageUnavailable, "/body/stages/0", "stage", "x", ["y"], null, null),
        };
        samples.Select(sample => sample.Code).Should().BeEquivalentTo(CatalogCheck.Codes);
        foreach (var sample in samples)
        {
            var finding = ReadinessChecks.CatalogFinding(sample, Obj(ReferenceDocs.Text(NW)));
            finding.TextEn.Should().NotBeNullOrWhiteSpace(sample.Code).And.NotContain("{");
            finding.TextEs.Should().NotBeNullOrWhiteSpace(sample.Code).And.NotContain("{");
        }
    }
}

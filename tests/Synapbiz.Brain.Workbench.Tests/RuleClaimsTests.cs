using System.Text.Json.Nodes;
using FluentAssertions;

namespace Synapbiz.Brain.Workbench.Tests;

public sealed class RuleClaimsTests
{
    private const string Cited = """
        {"authority":"recommendation","sources":[
          {"source":"guide.alpha","version":"2024","relation":"adapts","differences":[{"en":"Uses a lower threshold.","es":"Usa un umbral menor."}]},
          {"source":"guide.beta","version":"1.2","relation":"implements"}]}
        """;

    private static JsonObject Meta(string json) => (JsonObject)JsonNode.Parse(json)!;

    private static string WithMeta(string metaJson)
    {
        var document = (JsonObject)JsonNode.Parse(TestFiles.IntakeText("intake.first.rule.json"))!;
        var meta = (JsonObject)document["meta"]!;
        foreach (var (key, value) in Meta(metaJson))
        {
            meta[key] = value?.DeepClone();
        }

        return document.ToJsonString();
    }

    [Fact]
    public void Sources_and_authority_are_read_in_document_order()
    {
        var claims = RuleClaims.From(Meta(Cited));

        claims.Authority.Should().Be("recommendation");
        claims.Sources.Should().HaveCount(2).And.OnlyContain(entry => entry.IsReadable);
        claims.Readable.Select(source => source.Source).Should().Equal("guide.alpha", "guide.beta");
        var alpha = claims.Readable.First();
        alpha.Version.Should().Be("2024");
        alpha.Relation.Should().Be(SourceRelation.Adapts);
        alpha.Differences.Single().Text(WorkbenchLocale.Es).Should().Be("Usa un umbral menor.");
        alpha.Differences.Single().Text(WorkbenchLocale.En).Should().Be("Uses a lower threshold.");
        claims.HasAny.Should().BeTrue();
    }

    [Fact]
    public void A_version_written_as_a_number_is_read_as_text()
    {
        var claims = RuleClaims.From(Meta("""{"sources":[{"source":"guide.alpha","version":2024,"relation":"implements"}]}"""));

        claims.Readable.Single().Version.Should().Be("2024");
    }

    [Fact]
    public void A_meta_without_claims_reads_as_empty()
    {
        var claims = RuleClaims.From(Meta("""{"title":"x"}"""));

        claims.Sources.Should().BeEmpty();
        claims.Authority.Should().BeNull();
        claims.HasAny.Should().BeFalse();
        RuleClaims.Empty.HasAny.Should().BeFalse();
    }

    [Theory]
    [InlineData("""{"sources":[{"source":"a","version":"1","relation":"copies"}]}""")]
    [InlineData("""{"sources":[{"source":"a","relation":"adapts"}]}""")]
    [InlineData("""{"sources":["a"]}""")]
    [InlineData("""{"sources":[{"source":"a","version":"1","relation":"adapts","differences":"none"}]}""")]
    [InlineData("""{"sources":[{"source":"a","version":"1","relation":"adapts","differences":[{"en":"x"}]}]}""")]
    [InlineData("""{"sources":{"source":"a"}}""")]
    public void An_entry_of_the_wrong_shape_is_unreadable_and_never_throws(string json)
    {
        var claims = RuleClaims.From(Meta(json));

        claims.Sources.Should().ContainSingle().Which.IsReadable.Should().BeFalse();
        claims.Readable.Should().BeEmpty();
    }

    [Fact]
    public void A_bad_entry_does_not_hide_the_good_ones_and_keeps_its_position()
    {
        var claims = RuleClaims.From(Meta("""{"sources":[{"source":"a","version":"1","relation":"nope"},{"source":"b","version":"1","relation":"implements"}]}"""));

        claims.Sources.Select(entry => (entry.Index, entry.IsReadable)).Should().Equal((0, false), (1, true));
    }

    [Fact]
    public void An_authority_that_is_not_a_text_is_ignored()
    {
        RuleClaims.From(Meta("""{"authority":3}""")).Authority.Should().BeNull();
        RuleClaims.From(Meta("""{"authority":"  "}""")).Authority.Should().BeNull();
        RuleClaims.From(Meta("""{"authority":" payment_condition "}""")).Authority.Should().Be("payment_condition");
    }

    [Theory]
    [InlineData(SourceRelation.Implements, "implements", "implementa")]
    [InlineData(SourceRelation.Adapts, "adapts", "adapta")]
    [InlineData(SourceRelation.InformedBy, "is informed by", "se inspira en")]
    public void The_relation_has_a_word_in_both_languages(SourceRelation relation, string en, string es)
    {
        RuleClaims.RelationWord(relation, WorkbenchLocale.En).Should().Be(en);
        RuleClaims.RelationWord(relation, WorkbenchLocale.Es).Should().Be(es);
    }

    [Fact]
    public void Identical_claims_do_not_differ_and_say_nothing()
    {
        RuleClaims.Differ(Meta(Cited), Meta(Cited)).Should().BeFalse();
        RuleClaims.Changes(Meta(Cited), Meta(Cited), WorkbenchLocale.En).Should().BeEmpty();
    }

    [Fact]
    public void A_change_of_another_member_of_meta_is_not_a_change_of_claims()
    {
        RuleClaims.Differ(Meta("""{"title":"a"}"""), Meta("""{"title":"b"}""")).Should().BeFalse();
    }

    [Fact]
    public void Changes_are_told_in_plain_language_in_both_languages()
    {
        var before = Meta("""{"authority":"recommendation","sources":[{"source":"a","version":"1","relation":"implements"},{"source":"b","version":"1","relation":"adapts","differences":[{"en":"x","es":"x"}]},{"source":"c","version":"1","relation":"implements"}]}""");
        var after = Meta("""{"authority":"payer_measured","sources":[{"source":"a","version":"2","relation":"adapts"},{"source":"b","version":"1","relation":"adapts","differences":[{"en":"y","es":"y"}]},{"source":"d","version":"3","relation":"informed_by"}]}""");

        RuleClaims.Changes(before, after, WorkbenchLocale.En).Should().Equal(
            "The rule now cites the source `d`, version 3, which it is informed by.",
            "The rule no longer cites the source `c`.",
            "The rule follows the source `a` in version 2 instead of version 1.",
            "The rule adapts the source `a` where it used to implements.",
            "The differences from the source `b` changed.",
            "The authority of the rule changed from `recommendation` to `payer_measured`.");
        RuleClaims.Changes(before, after, WorkbenchLocale.Es).Should().HaveCount(6).And.OnlyContain(sentence => sentence.Contains("regla") || sentence.Contains("fuente"));
    }

    [Fact]
    public void The_authority_set_and_cleared_are_told()
    {
        var none = Meta("""{}""");
        var some = Meta("""{"authority":"local_preference"}""");

        RuleClaims.Changes(none, some, WorkbenchLocale.En).Should().Equal("The rule now states its authority: `local_preference`.");
        RuleClaims.Changes(some, none, WorkbenchLocale.En).Should().Equal("The rule no longer states its authority (`local_preference`).");
        RuleClaims.Changes(none, some, WorkbenchLocale.Es).Should().ContainSingle().Which.Should().Contain("autoridad");
    }

    [Fact]
    public void A_change_in_an_unreadable_entry_gets_one_general_sentence()
    {
        var before = Meta("""{"sources":[{"source":"a"}]}""");
        var after = Meta("""{"sources":[{"source":"b"}]}""");

        RuleClaims.Differ(before, after).Should().BeTrue();
        RuleClaims.Changes(before, after, WorkbenchLocale.En).Should().Equal("What the rule says about its sources changed.");
    }

    [Fact]
    public void The_differ_reports_a_change_of_claims_and_it_counts_as_a_change()
    {
        var plain = TestFiles.LoadIntakeText(WithMeta("{}"));
        var cited = TestFiles.LoadIntakeText(WithMeta(Cited));

        RuleDiffer.Diff(plain, plain).ClaimsChanged.Should().BeFalse();
        var diff = RuleDiffer.Diff(plain, cited);
        diff.ClaimsChanged.Should().BeTrue();
        diff.HasChanges.Should().BeTrue();
        diff.Rows.Should().OnlyContain(row => row.Kind == ChangeKind.Unchanged);
    }

    [Fact]
    public void The_description_carries_the_claim_sentences_alone_or_after_the_row_summary()
    {
        var plain = TestFiles.LoadIntakeText(WithMeta("{}"));
        var cited = TestFiles.LoadIntakeText(WithMeta("""{"authority":"recommendation"}"""));
        var labels = RuleLabels.FromMeta(plain.Meta, WorkbenchLocale.En);

        var only = RuleChangeDescriber.Describe(plain, cited, labels, labels, WorkbenchLocale.En);
        only.ClaimChanges.Should().Equal("The rule now states its authority: `recommendation`.");
        only.Summary.Should().Be("The rule now states its authority: `recommendation`.");

        var none = RuleChangeDescriber.Describe(plain, plain, labels, labels, WorkbenchLocale.En);
        none.ClaimChanges.Should().BeEmpty();

        var v2 = TestFiles.LoadIntake("intake.first.v2.rule.json");
        var withRows = RuleChangeDescriber.Describe(TestFiles.LoadIntake("intake.first.rule.json"), v2, labels, labels, WorkbenchLocale.En);
        withRows.ClaimChanges.Should().BeEmpty();
    }

    [Fact]
    public void A_change_of_the_authority_alone_is_a_difference_and_the_same_authority_is_not()
    {
        var before = Meta("""{"title":"a","authority":"recommendation"}""");

        RuleClaims.Differ(before, Meta("""{"title":"a","authority":"payer_measured"}""")).Should().BeTrue();
        RuleClaims.Differ(before, Meta("""{"title":"a"}""")).Should().BeTrue();
        RuleClaims.Differ(Meta("""{"title":"a"}"""), before).Should().BeTrue();
        RuleClaims.Differ(before, Meta("""{"title":"b","authority":"recommendation"}""")).Should().BeFalse("only the claims count");
        RuleClaims.Changes(before, Meta("""{"title":"a","authority":"payer_measured"}"""), WorkbenchLocale.En)
            .Should().Equal("The authority of the rule changed from `recommendation` to `payer_measured`.");
    }

    [Fact]
    public void The_order_of_the_members_of_a_source_entry_does_not_make_a_difference_but_a_value_does()
    {
        var one = Meta("""{"sources":[{"source":"a","version":"1","relation":"adapts","differences":[{"en":"x","es":"y"}]}]}""");
        var reordered = Meta("""{"sources":[{"differences":[{"es":"y","en":"x"}],"relation":"adapts","version":"1","source":"a"}]}""");
        var other = Meta("""{"sources":[{"differences":[{"es":"z","en":"x"}],"relation":"adapts","version":"1","source":"a"}]}""");
        var swapped = Meta("""{"sources":[{"source":"a","version":"1","relation":"implements"},{"source":"b","version":"1","relation":"implements"}]}""");
        var swappedBack = Meta("""{"sources":[{"source":"b","version":"1","relation":"implements"},{"source":"a","version":"1","relation":"implements"}]}""");

        RuleClaims.Differ(one, reordered).Should().BeFalse();
        RuleClaims.Differ(one, other).Should().BeTrue();
        RuleClaims.Differ(swapped, swappedBack).Should().BeTrue("the order of the entries is the document's");
    }
}

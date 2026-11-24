using System.Text.Json.Nodes;
using FluentAssertions;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;
using static Synapbiz.Brain.Authoring.Tests.Fixtures;

namespace Synapbiz.Brain.Authoring.Tests;

public sealed class RuleClaimsToolTests
{
    private const string Claims = """
        {"authority":"recommendation","sources":[
          {"source":"guide.alpha","version":"2024","relation":"adapts","differences":[{"en":"Uses a lower threshold.","es":"Usa un umbral menor."}]},
          {"source":"guide.alpha","version":"bad"}]}
        """;

    private static string WithMeta(string text, string metaJson)
    {
        var document = Obj(text);
        var meta = (JsonObject)document["meta"]!;
        foreach (var (key, value) in Obj(metaJson))
        {
            meta[key] = value?.DeepClone();
        }

        return document.ToJsonString();
    }

    private static AuthoringTools Tools()
    {
        var catalog = NoteCatalog();
        var store = new InMemoryAuthoringStore(
            new Dictionary<string, string> { ["visit.note_wait"] = NoteWait },
            new Dictionary<string, string>
            {
                ["visit.note_ready"] = WithMeta(NoteReady, Claims),
                ["intake-routing"] = WithMeta(IntakeRule, """{"authority":"payment_condition"}"""),
            },
            CatalogJson.Serialize(catalog));
        return new AuthoringTools(store, new AuthoringOptions { Clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Start) });
    }

    private static BrainRule Rule(string text, JsonObject contract) =>
        BrainRuleLoader.Load(text, [BrainContract.Parse(contract.ToJsonString())]).Rule!;

    [Fact]
    public void A_change_of_sources_or_authority_needs_review_with_its_own_reason()
    {
        var plain = Rule(IntakeRule, IntakeContract);
        var cited = Rule(WithMeta(IntakeRule, Claims), IntakeContract);
        var authority = Rule(WithMeta(IntakeRule, """{"authority":"local_preference"}"""), IntakeContract);

        RuleReviewNeed.Reasons(plain, plain).Should().BeEmpty();
        RuleReviewNeed.Reasons(plain, cited).Should().Equal(RuleReviewNeed.ClaimsChanged);
        RuleReviewNeed.Reasons(cited, plain).Should().Equal(RuleReviewNeed.ClaimsChanged);
        RuleReviewNeed.Reasons(plain, authority).Should().Equal(RuleReviewNeed.ClaimsChanged);
        RuleReviewNeed.Reasons(cited, cited).Should().BeEmpty();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public void The_reason_has_words(string language)
    {
        var locale = language == "es" ? WorkbenchLocale.Es : WorkbenchLocale.En;
        AuthoringText.Text(locale, "rule.review." + RuleReviewNeed.ClaimsChanged).Should().NotBeNullOrWhiteSpace();
        AuthoringText.Text(locale, "rule.source.unreadable").Should().NotBeNullOrWhiteSpace();
        foreach (var key in new[] { "say.rule.list.source", "say.rule.list.authority", "say.rule.list.both" })
        {
            AuthoringText.Text(locale, key).Should().Contain("{n}");
        }
    }

    [Fact]
    public void Rule_list_filters_by_source_and_says_how_many_rules_cite_it()
    {
        var tools = Tools();

        var bySource = tools.Invoke("rule_list", Obj("""{"source":"guide.alpha"}"""));
        IsOk(bySource).Should().BeTrue(bySource.ToJsonString());
        bySource["rules"]!.AsArray().Select(item => item!["id"]!.GetValue<string>()).Should().Equal("visit.note_ready");
        bySource["say"]!.GetValue<string>().Should().Contain("Rules that cite the source").And.Contain("guide.alpha").And.EndWith(": 1.");

        var none = tools.Invoke("rule_list", Obj("""{"source":"guide.unknown","language":"es"}"""));
        none["rules"]!.AsArray().Should().BeEmpty();
        none["say"]!.GetValue<string>().Should().Contain("Reglas que citan la fuente").And.EndWith(": 0.");
    }

    [Fact]
    public void Rule_list_filters_by_authority_and_by_both()
    {
        var tools = Tools();

        var byAuthority = tools.Invoke("rule_list", Obj("""{"authority":"payment_condition"}"""));
        byAuthority["rules"]!.AsArray().Select(item => item!["id"]!.GetValue<string>()).Should().Equal("intake-routing");
        byAuthority["say"]!.GetValue<string>().Should().Contain("state the authority");

        var both = tools.Invoke("rule_list", Obj("""{"source":"guide.alpha","authority":"recommendation"}"""));
        both["rules"]!.AsArray().Should().ContainSingle();
        both["say"]!.GetValue<string>().Should().Contain("and state the authority");

        var mismatch = tools.Invoke("rule_list", Obj("""{"source":"guide.alpha","authority":"payment_condition"}"""));
        mismatch["rules"]!.AsArray().Should().BeEmpty();

        var all = tools.Invoke("rule_list", new JsonObject());
        all["rules"]!.AsArray().Should().HaveCount(2);
        all["rules"]!.AsArray()[0]!["authority"]!.GetValue<string>().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Rule_get_gives_the_sources_the_unreadable_entry_and_the_authority()
    {
        var tools = Tools();

        var answer = tools.Invoke("rule_get", Obj("""{"id":"visit.note_ready","language":"es"}"""));
        IsOk(answer).Should().BeTrue(answer.ToJsonString());
        var view = answer["view"]!;
        view["authority"]!.GetValue<string>().Should().Be("recommendation");
        var sources = view["sources"]!.AsArray();
        sources.Should().HaveCount(2);
        sources[0]!["readable"]!.GetValue<bool>().Should().BeTrue();
        sources[0]!["source"]!.GetValue<string>().Should().Be("guide.alpha");
        sources[0]!["version"]!.GetValue<string>().Should().Be("2024");
        sources[0]!["relation"]!.GetValue<string>().Should().Be("adapts");
        sources[0]!["relationWord"]!.GetValue<string>().Should().Be("adapta");
        sources[0]!["differences"]!.AsArray().Single()!.GetValue<string>().Should().Be("Usa un umbral menor.");
        sources[1]!["readable"]!.GetValue<bool>().Should().BeFalse();
        sources[1]!["say"]!.GetValue<string>().Should().Be("entrada de fuente no legible");
        answer["say"]!.GetValue<string>().Should().EndWith("Una entrada de fuente de esta regla no se puede leer (véanse sus fuentes).");
        tools.Invoke("rule_get", Obj("""{"id":"visit.note_ready"}"""))["say"]!.GetValue<string>().Should().EndWith("One source entry of this rule cannot be read (see its sources).");
        tools.Invoke("rule_get", Obj("""{"id":"intake-routing"}"""))["say"]!.GetValue<string>().Should().NotContain("cannot be read");
    }

    [Fact]
    public void Rule_list_says_in_words_which_rules_have_a_source_entry_that_cannot_be_read()
    {
        var tools = Tools();

        var all = tools.Invoke("rule_list", new JsonObject());
        all["say"]!.GetValue<string>().Should().Contain("Rules with a source entry that cannot be read: “visit.note_ready”.");
        var ready = all["rules"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == "visit.note_ready")!;
        ready["unreadableSources"]!.GetValue<int>().Should().Be(1);
        ready["sources"]!.AsArray().Should().HaveCount(1, "only the readable ones are named");
        all["rules"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == "intake-routing")!["unreadableSources"]!.GetValue<int>().Should().Be(0);

        tools.Invoke("rule_list", Obj("""{"language":"es"}"""))["say"]!.GetValue<string>().Should().Contain("Reglas con una entrada de fuente que no se puede leer:");
        tools.Invoke("rule_list", Obj("""{"authority":"payment_condition"}"""))["say"]!.GetValue<string>().Should().NotContain("cannot be read");
    }

    [Fact]
    public void A_source_entry_with_the_members_in_another_order_is_not_a_change_that_needs_review()
    {
        var one = Rule(WithMeta(IntakeRule, """{"sources":[{"source":"a","version":"1","relation":"adapts","differences":[{"en":"x","es":"y"}]}]}"""), IntakeContract);
        var reordered = Rule(WithMeta(IntakeRule, """{"sources":[{"differences":[{"es":"y","en":"x"}],"relation":"adapts","version":"1","source":"a"}]}"""), IntakeContract);

        RuleReviewNeed.Reasons(one, reordered).Should().BeEmpty();
    }

    [Fact]
    public void Rule_get_of_a_rule_without_claims_has_none()
    {
        var (tools, _, _) = Rig();

        var view = tools.Invoke("rule_get", Obj("""{"id":"visit.note_ready"}"""))["view"]!;
        view["sources"]!.AsArray().Should().BeEmpty();
        view["authority"].Should().BeNull();
    }
}

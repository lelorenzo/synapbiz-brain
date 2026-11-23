using FluentAssertions;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>The loader's public contract.</summary>
public sealed class LoaderTests
{
    private const string Variables = """{ "n": { "type": "Integer" } }""";

    private const string Steps = """{ "s": { "kind": "end", "result": "ok" } }""";

    private static readonly ProcessLoadContext Empty = new([]);

    [Fact]
    public void NullArgumentsThrow()
    {
        var noText = () => BrainProcessLoader.Load(null!, Empty);
        var noContext = () => BrainProcessLoader.Load(Docs.Text(Variables, Steps), null!);

        noText.Should().Throw<ArgumentNullException>();
        noContext.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AValidDocumentLoadsWithNoErrors()
    {
        var loaded = BrainProcessLoader.Load(Docs.Text(Variables, Steps), Empty);

        loaded.IsValid.Should().BeTrue();
        loaded.Errors.Should().BeEmpty();
        loaded.Process.Should().NotBeNull();
        loaded.Process!.Id.Should().Be("t.proc");
    }

    [Fact]
    public void AnInvalidDocumentHasErrorsAndNoProcess()
    {
        var loaded = BrainProcessLoader.Load("{ not json", Empty);

        loaded.IsValid.Should().BeFalse();
        loaded.Process.Should().BeNull();
        loaded.Errors.Should().ContainSingle().Which.Code.Should().Be("spec.json.invalid");
    }

    [Fact]
    public void TheErrorsCarryBothLanguagesAndAHintAndAreOrdered()
    {
        var steps = """{ "s": { "kind": "read", "port": "p", "next": "missing", "stage": "nowhere" }, "x": { "kind": "end", "result": "r" } }""";

        var loaded = BrainProcessLoader.Load(Docs.Text(Variables, steps), Empty);

        loaded.Errors.Should().HaveCountGreaterThan(2);
        loaded.Errors.Should().OnlyContain(error => error.MessageEn.Length > 0 && error.MessageEs.Length > 0 && error.Path.StartsWith('/'));
        loaded.Errors.Select(error => error.Path + "|" + error.Code).Should().BeInAscendingOrder(StringComparer.Ordinal);
        loaded.Errors.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void OnlyTheFirstGroupOfErrorsIsReported()
    {
        var steps = """{ "s": { "kind": "end", "result": "r", "bogus": 1 }, "x": { "kind": "end", "result": "r" } }""";

        var loaded = BrainProcessLoader.Load(Docs.Text(Variables, steps), Empty);

        loaded.Errors.Should().OnlyContain(error => error.Code == "spec.structure.schema");
    }

    [Fact]
    public void TheContentHashFollowsTheText()
    {
        var first = Docs.Load(Variables, Steps);
        var again = Docs.Load(Variables, Steps);
        var other = Docs.Load(Variables, Steps.Replace("\"ok\"", "\"fine\"", StringComparison.Ordinal));

        first.ContentHash.Should().Be(again.ContentHash);
        first.ContentHash.Should().NotBe(other.ContentHash);
        first.ContentHash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void TheMetaIsReturnedAsACloneEachTime()
    {
        var text = Docs.Text(Variables, Steps).Replace("\"meta\": {}", "\"meta\": { \"title\": \"Case flow\", \"tags\": [\"a\"] }", StringComparison.Ordinal);
        var process = BrainProcessLoader.Load(text, Empty).Process!;

        var first = process.Meta;
        first["title"] = "changed";
        first["tags"]!.AsArray().Add("b");
        var second = process.Meta;

        second["title"]!.GetValue<string>().Should().Be("Case flow");
        second["tags"]!.AsArray().Should().HaveCount(1);
        second.Should().NotBeSameAs(first);
    }

    [Fact]
    public void ChildProcessIdsAreCheckedOnlyWhenTheContextSuppliesThem()
    {
        var steps = """{ "s": { "kind": "subprocess", "process": "child.proc", "next": "e" }, "e": { "kind": "end", "result": "ok" } }""";
        var text = Docs.Text(Variables, steps, uses: """[{ "$ref": "process/child.proc" }]""");

        BrainProcessLoader.Load(text, Empty).IsValid.Should().BeTrue();
        BrainProcessLoader.Load(text, new ProcessLoadContext([], ["child.proc", "other"])).IsValid.Should().BeTrue();
        BrainProcessLoader.Load(text, new ProcessLoadContext([], ["other"])).Errors.Should().ContainSingle().Which.Code.Should().Be("process.subprocess.unavailable");
        BrainProcessLoader.Load(text, new ProcessLoadContext([], [])).Errors.Should().ContainSingle().Which.Code.Should().Be("process.subprocess.unavailable");
    }

    [Fact]
    public void ARuleIsFoundInTheContextByItsId()
    {
        var steps = """{ "s": { "kind": "decide", "rule": "conf.flag_rule", "facts": { "count": "n" }, "next": "e", "onNoDecision": "e" }, "e": { "kind": "end", "result": "ok" } }""";
        var text = Docs.Text(Variables, steps, uses: """[{ "$ref": "rule/conf.flag_rule" }]""");

        BrainProcessLoader.Load(text, new ProcessLoadContext([Docs.FlagRule, Docs.ListRule])).IsValid.Should().BeTrue();
        BrainProcessLoader.Load(text, new ProcessLoadContext([Docs.ListRule])).Errors.Select(error => error.Code).Should().Contain("process.rule.unavailable");
        BrainProcessLoader.Load(text, Empty).Errors.Select(error => error.Code).Should().Contain("process.rule.unavailable");
    }

    [Fact]
    public void TheProcessKeepsItsOwnCopyOfTheRulesItUses()
    {
        var steps = """{ "s": { "kind": "decide", "rule": "conf.flag_rule", "facts": { "count": "n" }, "next": "e", "onNoDecision": "e" }, "e": { "kind": "end", "result": "ok" } }""";
        var text = Docs.Text(Variables, steps, uses: """[{ "$ref": "rule/conf.flag_rule" }]""");
        var rules = new List<Synapbiz.Brain.Spec.BrainRule> { Docs.FlagRule };
        var process = BrainProcessLoader.Load(text, new ProcessLoadContext(rules)).Process!;

        rules.Clear();
        var started = process.Start("i-1", null, Docs.Clock());

        started.State.Status.Should().Be(ProcessStatus.Suspended);
    }

    [Fact]
    public void AnUnknownFormatVersionIsReported()
    {
        var text = Docs.Text(Variables, Steps).Replace("brain.process@1", "brain.process@2", StringComparison.Ordinal);

        var loaded = BrainProcessLoader.Load(text, Empty);

        loaded.Errors.Should().ContainSingle().Which.Code.Should().Be("spec.structure.unknown_format");
    }
}

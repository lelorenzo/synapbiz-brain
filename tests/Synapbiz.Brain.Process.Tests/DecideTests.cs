using System.Text.Json.Nodes;
using FluentAssertions;
using Synapbiz.Brain.Spec;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>Section 6.3: decide steps.</summary>
public sealed class DecideTests
{
    private const string Uses = """[{ "$ref": "rule/conf.flag_rule" }]""";

    private const string Variables = """
        {
          "count": { "type": "Integer", "input": true },
          "note": { "type": "Code" },
          "level": { "type": "Enum", "allowedValues": ["low", "high"] },
          "total": { "type": "Integer" },
          "day": { "type": "Date" },
          "owner": { "type": "Reference" },
          "ratio": { "type": "Decimal" },
          "price": { "type": "Money" },
          "flag": { "type": "Boolean" },
          "list": { "type": "Json" }
        }
        """;

    private static readonly string[] EndIds = ["yes", "no", "none", "err"];

    private static Driver Started(string decide, string input = """{ "count": 10 }""")
    {
        var ends = EndIds
            .Where(id => decide.Contains($": \"{id}\"", StringComparison.Ordinal))
            .Select(id => $$"""
                "{{id}}": { "kind": "end", "result": "{{id}}" }
                """);
        var steps = "{ \"s\": " + decide + ", " + string.Join(", ", ends) + " }";
        var driver = new Driver(Docs.Load(Variables, steps, uses: Uses, rules: Docs.FlagRule));
        driver.Start(input);
        return driver;
    }

    private static string Flag(string extra = "\"output\": { \"note\": \"note\" }, \"route\": \"flag\", \"on\": { \"true\": \"yes\", \"false\": \"no\" }", string errors = ", \"onError\": \"err\"") =>
        $$"""{ "kind": "decide", "rule": "conf.flag_rule", "facts": { "count": "count" }, {{extra}}, "onNoDecision": "none"{{errors}} }""";

    [Fact]
    public void ABooleanOutcomeRoutesByTrueAndFalse()
    {
        Started(Flag()).State.Result.Should().Be("yes");
        Started(Flag(), """{ "count": 3 }""").State.Result.Should().Be("no");
    }

    [Fact]
    public void OutputMembersAreAssignedAndNullClearsTheVariable()
    {
        var big = Started(Flag("\"output\": { \"note\": \"note\", \"level\": \"level\", \"total\": \"total\", \"when\": \"day\", \"owner\": \"owner\", \"flag\": \"flag\" }, \"next\": \"yes\""));

        big.Variable("note").Should().Be("\"big\"");
        big.Variable("level").Should().Be("\"high\"");
        big.Variable("total").Should().Be("10");
        big.Variable("day").Should().Be("\"2027-01-04\"");
        big.Variable("owner").Should().Be("\"00000000-0000-0000-0000-000000000001\"");
        big.Variable("flag").Should().Be("true");
        big.State.Result.Should().Be("yes");

        var small = Started(Flag("\"output\": { \"note\": \"note\", \"total\": \"total\" }, \"next\": \"yes\""), """{ "count": 3 }""");

        small.State.Variables.Should().NotContainKey("note");
        small.State.Variables.Should().NotContainKey("total");
    }

    [Fact]
    public void AnEnumOutcomeRoutesByItsValue()
    {
        var driver = Started(Flag("\"route\": \"level\", \"on\": { \"high\": \"yes\", \"low\": \"no\" }"));

        driver.State.Result.Should().Be("yes");
    }

    [Fact]
    public void ACodeOutcomeRoutesByItsValueAndNullIsUnmatched()
    {
        var steps = Flag("\"route\": \"note\", \"on\": { \"big\": \"yes\" }");

        Started(steps).State.Result.Should().Be("yes");
        Started(steps, """{ "count": 3 }""").State.Result.Should().Be("err");
    }

    [Fact]
    public void AnUnmatchedRouteSuspendsWithoutOnError()
    {
        var driver = Started(Flag("\"route\": \"note\", \"on\": { \"big\": \"yes\" }", errors: ""), """{ "count": 3 }""");

        driver.State.Status.Should().Be(ProcessStatus.Suspended);
        driver.State.Problem.Should().Be(new ProcessProblem("process.route.unmatched", "s"));
        driver.State.CurrentStep.Should().Be("s");
    }

    [Fact]
    public void NoDecisionGoesToOnNoDecisionAndIsLogged()
    {
        var driver = Started(Flag(), """{ "count": -1 }""");

        driver.State.Result.Should().Be("none");
        var decided = JsonNode.Parse(driver.Last.History.Single(entry => entry.Kind == ProcessHistoryKind.Decided).DataJson!)!.AsObject();
        decided["decided"]!.GetValue<bool>().Should().BeFalse();
        decided["outcome"].Should().BeNull();
        decided["matchedRowKeys"]!.AsArray().Should().BeEmpty();
    }

    [Fact]
    public void TheDecidedEntryCarriesTheProvenance()
    {
        var driver = Started(Flag());

        var entry = driver.Last.History.Single(item => item.Kind == ProcessHistoryKind.Decided);
        var data = JsonNode.Parse(entry.DataJson!)!.AsObject();

        entry.Step.Should().Be("s");
        data["rule"]!.GetValue<string>().Should().Be("conf.flag_rule");
        data["version"]!.GetValue<long>().Should().Be(1);
        data["decided"]!.GetValue<bool>().Should().BeTrue();
        data["matchedRowKeys"]!.AsArray().Select(key => key!.GetValue<string>()).Should().Equal("big");
        data["factsHash"]!.GetValue<string>().Should().MatchRegex("^[0-9a-f]{64}$");
        data["facts"]!.ToJsonString().Should().Contain("count").And.Contain("10");
        data["outcome"]!["note"]!.GetValue<string>().Should().Be("big");
    }

    [Fact]
    public void TheFactsHashIsStableForTheSameFactsAndDiffersForOthers()
    {
        string Hash(Driver driver) => JsonNode.Parse(driver.Last.History.Single(item => item.Kind == ProcessHistoryKind.Decided).DataJson!)!["factsHash"]!.GetValue<string>();

        Hash(Started(Flag())).Should().Be(Hash(Started(Flag())));
        Hash(Started(Flag())).Should().NotBe(Hash(Started(Flag(), """{ "count": 11 }""")));
    }

    [Fact]
    public void AMissingRequiredFactIsAnEngineErrorRoutedToOnError()
    {
        var driver = Started(Flag(), "{}");

        driver.State.Result.Should().Be("err");
        AssertFailedDecision(driver);
    }

    [Fact]
    public void AnEngineErrorWithoutOnErrorSuspendsWithTheErrorCode()
    {
        var driver = Started(Flag(errors: ""), "{}");

        driver.State.Status.Should().Be(ProcessStatus.Suspended);
        driver.State.Problem!.Step.Should().Be("s");
        driver.State.Problem.Code.Should().NotBeNullOrEmpty().And.NotBe("process.route.unmatched");
        driver.State.Problem.Detail.Should().NotBeNullOrEmpty();
        driver.Last.History[^1].Kind.Should().Be(ProcessHistoryKind.Suspended);
        AssertFailedDecision(driver);
        driver.Last.History.Single(entry => entry.Kind == ProcessHistoryKind.Decided).Code.Should().Be(driver.State.Problem.Code);
    }

    private static void AssertFailedDecision(Driver driver)
    {
        var entry = driver.Last.History.Single(item => item.Kind == ProcessHistoryKind.Decided);
        var data = JsonNode.Parse(entry.DataJson!)!.AsObject();

        entry.Step.Should().Be("s");
        entry.Code.Should().NotBeNullOrEmpty();
        data.Select(member => member.Key).Should().Equal("error", "rule", "version");
        data["error"]!.GetValue<string>().Should().Be(entry.Code);
        data["rule"]!.GetValue<string>().Should().Be("conf.flag_rule");
        data["version"]!.GetValue<long>().Should().Be(1);
    }

    [Fact]
    public void DecimalAndMoneyOutcomesAreWrittenAsJsonNumbersByTheRuleLibrary()
    {
        var driver = Started(Flag());
        var outcome = JsonNode.Parse(JsonNode.Parse(driver.Last.History.Single(item => item.Kind == ProcessHistoryKind.Decided).DataJson!)!["outcome"]!.ToJsonString())!.AsObject();

        outcome["ratio"]!.GetValueKind().Should().Be(System.Text.Json.JsonValueKind.Number);
        outcome["price"]!["amount"]!.GetValueKind().Should().Be(System.Text.Json.JsonValueKind.Number);
        outcome["price"]!["currency"]!.GetValue<string>().Should().Be("USD");
        outcome["flag"]!.GetValueKind().Should().Be(System.Text.Json.JsonValueKind.True);
        outcome["total"]!.GetValueKind().Should().Be(System.Text.Json.JsonValueKind.Number);
        outcome["when"]!.GetValue<string>().Should().Be("2027-01-04");
        outcome["owner"]!.GetValue<string>().Should().Be("00000000-0000-0000-0000-000000000001");
    }

    [Fact]
    public void ADecimalOutcomeBecomesTheRawLexemeAsAString()
    {
        var driver = Started(Flag("\"output\": { \"ratio\": \"ratio\" }, \"next\": \"yes\""));

        driver.State.Result.Should().Be("yes");
        driver.Variable("ratio").Should().Be("\"1.5\"");
    }

    [Fact]
    public void AMoneyOutcomeBecomesAnAmountStringAndTheCurrency()
    {
        var driver = Started(Flag("\"output\": { \"price\": \"price\" }, \"next\": \"yes\""));

        driver.State.Result.Should().Be("yes");
        driver.Variable("price").Should().Be("{\"amount\":\"2.5\",\"currency\":\"USD\"}");
    }

    [Theory]
    [InlineData("0.10")]
    [InlineData("-12.345678901234567890123456")]
    [InlineData("3.1415926535897932384626433")]
    [InlineData("0")]
    public void TheLexemeIsKeptExactlyAndNeverGoesThroughAFloatingType(string lexeme)
    {
        var rule = Docs.RuleFrom(RuleWith($$"""{ "flag": true, "ratio": {{lexeme}}, "price": { "amount": {{lexeme}}, "currency": "EUR" } }"""));
        var driver = Run(rule, "\"output\": { \"ratio\": \"ratio\", \"price\": \"price\" }, \"next\": \"yes\"");

        driver.State.Result.Should().Be("yes");
        driver.Variable("ratio").Should().Be("\"" + lexeme + "\"");
        driver.Variable("price").Should().Be("{\"amount\":\"" + lexeme + "\",\"currency\":\"EUR\"}");
    }

    [Theory]
    [InlineData("1E2")]
    [InlineData("1.5e-3")]
    public void ANumberInExponentNotationIsATypeMismatch(string lexeme)
    {
        var rule = Docs.RuleFrom(RuleWith($$"""{ "flag": true, "ratio": {{lexeme}} }"""));
        var driver = Run(rule, "\"output\": { \"ratio\": \"ratio\" }, \"next\": \"yes\"");

        driver.State.Result.Should().Be("err");
        driver.State.Variables.Should().NotContainKey("ratio");
    }

    [Fact]
    public void AMoneyAmountInExponentNotationIsATypeMismatch()
    {
        var rule = Docs.RuleFrom(RuleWith("""{ "flag": true, "price": { "amount": 2E1, "currency": "USD" } }"""));
        var driver = Run(rule, "\"output\": { \"price\": \"price\" }, \"next\": \"yes\"");

        driver.State.Result.Should().Be("err");
        driver.State.Variables.Should().NotContainKey("price");
    }

    [Fact]
    public void ATypeMismatchOnAnOutcomeWithoutOnErrorSuspends()
    {
        var rule = Docs.RuleFrom(RuleWith("""{ "flag": true, "ratio": 1E2 }"""));
        var driver = Run(rule, "\"output\": { \"ratio\": \"ratio\" }, \"next\": \"yes\"", errors: "");

        driver.State.Status.Should().Be(ProcessStatus.Suspended);
        driver.State.Problem!.Code.Should().Be("process.variable.type_mismatch");
    }

    private static string RuleWith(string outcome) => $$"""
        {
          "$spec": "brain.rule@1", "id": "conf.exp_rule", "version": 3, "uses": [], "requires": ["conf@1"], "meta": {},
          "body": {
            "kind": "conf.flag", "hitPolicy": "first",
            "rows": [ { "id": "any", "version": 1, "legacyKey": "any",
              "conditions": [ { "fact": "count", "op": "range", "value": { "type": "Integer", "value": "0" } } ],
              "outcome": {{outcome}} } ],
            "goldens": []
          }
        }
        """;

    private static Driver Run(BrainRule rule, string extra, string errors = ", \"onError\": \"err\"")
    {
        var decide = $$"""{ "kind": "decide", "rule": "conf.exp_rule", "facts": { "count": "count" }, {{extra}}, "onNoDecision": "none"{{errors}} }""";
        var ends = EndIds.Where(id => decide.Contains($": \"{id}\"", StringComparison.Ordinal))
            .Select(id => $$"""
                "{{id}}": { "kind": "end", "result": "{{id}}" }
                """);
        var steps = "{ \"s\": " + decide + ", " + string.Join(", ", ends) + " }";
        var driver = new Driver(Docs.Load(Variables, steps, uses: """[{ "$ref": "rule/conf.exp_rule" }]""", rules: rule));
        driver.Start("""{ "count": 1 }""");
        return driver;
    }

    [Fact]
    public void AMemberThatTheRowDoesNotSetClearsTheVariable()
    {
        var driver = Started(Flag("\"output\": { \"price\": \"price\", \"total\": \"total\" }, \"next\": \"yes\""), """{ "count": 3 }""");

        driver.State.Result.Should().Be("yes");
        driver.State.Variables.Should().NotContainKey("price");
    }

    [Fact]
    public void ACollectRuleStoresTheOutcomeArray()
    {
        var steps = """
            {
              "s": { "kind": "decide", "rule": "conf.list_rule", "facts": { "count": "count" }, "collectInto": "list", "next": "e", "onNoDecision": "n" },
              "e": { "kind": "end", "result": "yes" },
              "n": { "kind": "end", "result": "none" }
            }
            """;
        var driver = new Driver(Docs.Load(Variables, steps, uses: """[{ "$ref": "rule/conf.list_rule" }]""", rules: Docs.ListRule));

        driver.Start("""{ "count": 7 }""");

        driver.State.Result.Should().Be("yes");
        driver.Variable("list").Should().Be("""[{"note":"counted"},{"note":"many"}]""");
    }

    [Fact]
    public void ACollectRuleWithNoRowsGoesToOnNoDecision()
    {
        var steps = """
            {
              "s": { "kind": "decide", "rule": "conf.list_rule", "facts": { "count": "count" }, "collectInto": "list", "next": "e", "onNoDecision": "n" },
              "e": { "kind": "end", "result": "yes" },
              "n": { "kind": "end", "result": "none" }
            }
            """;
        var driver = new Driver(Docs.Load(Variables, steps, uses: """[{ "$ref": "rule/conf.list_rule" }]""", rules: Docs.ListRule));

        driver.Start("""{ "count": -4 }""");

        driver.State.Result.Should().Be("none");
        driver.State.Variables.Should().NotContainKey("list");
    }

    [Fact]
    public void EveryFactTypeIsConvertedAndRecordedInTheProvenance()
    {
        var variables = """
            {
              "count": { "type": "Integer", "input": true },
              "label": { "type": "Code", "input": true },
              "mode": { "type": "Enum", "allowedValues": ["a", "b"], "input": true },
              "when": { "type": "Date", "input": true },
              "price": { "type": "Money", "input": true },
              "owner": { "type": "Reference", "input": true },
              "active": { "type": "Boolean", "input": true },
              "ratio": { "type": "Decimal", "input": true }
            }
            """;
        var steps = """
            {
              "s": { "kind": "decide", "rule": "conf.flag_rule",
                     "facts": { "count": "count", "label": "label", "mode": "mode", "when": "when", "price": "price", "owner": "owner", "active": "active", "ratio": "ratio" },
                     "next": "e", "onNoDecision": "e" },
              "e": { "kind": "end", "result": "ok" }
            }
            """;
        var driver = new Driver(Docs.Load(variables, steps, uses: Uses, rules: Docs.FlagRule));

        driver.Start("""
            { "count": 10, "label": "x", "mode": "b", "when": "2027-01-04", "price": { "amount": "12.50", "currency": "USD" },
              "owner": "00000000-0000-0000-0000-00000000000a", "active": true, "ratio": "0.25" }
            """);

        driver.State.Result.Should().Be("ok");
        var facts = JsonNode.Parse(JsonNode.Parse(driver.Last.History.Single(entry => entry.Kind == ProcessHistoryKind.Decided).DataJson!)!["facts"]!.ToJsonString())!.AsObject();
        facts.Select(member => member.Key).Should().BeEquivalentTo("count", "label", "mode", "when", "price", "owner", "active", "ratio");
    }

    [Fact]
    public void TheEvaluationDateIsTheClockToday()
    {
        var driver = Started(Flag());

        // The rule is date independent; the clock only has to be accepted. A different day gives the same decision.
        var other = new Driver(Docs.Load(Variables, """{ "s": { "kind": "end", "result": "yes" } }""", start: "s"));
        other.Now = Docs.T0.AddDays(40);
        other.Start();

        driver.State.Result.Should().Be("yes");
        other.State.Result.Should().Be("yes");
    }
}

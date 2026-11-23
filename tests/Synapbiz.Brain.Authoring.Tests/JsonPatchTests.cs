using System.Text.Json.Nodes;
using FluentAssertions;

namespace Synapbiz.Brain.Authoring.Tests;

/// <summary>RFC 6902, section 4 and the examples of appendix A.</summary>
public sealed class JsonPatchTests
{
    private static JsonNode Apply(string document, string patch) => JsonPatch.Apply(JsonNode.Parse(document)!, (JsonArray)JsonNode.Parse(patch)!);

    private static void Same(JsonNode actual, string expected) => JsonNode.DeepEquals(actual, JsonNode.Parse(expected)).Should().BeTrue(actual.ToJsonString());

    [Fact]
    public void Adds_an_object_member()
    {
        Same(Apply("""{"foo":"bar"}""", """[{"op":"add","path":"/baz","value":"qux"}]"""), """{"foo":"bar","baz":"qux"}""");
    }

    [Fact]
    public void Adds_an_array_element_at_a_position_and_at_the_end()
    {
        Same(Apply("""{"foo":["bar","baz"]}""", """[{"op":"add","path":"/foo/1","value":"qux"}]"""), """{"foo":["bar","qux","baz"]}""");
        Same(Apply("""{"foo":["bar"]}""", """[{"op":"add","path":"/foo/-","value":"qux"}]"""), """{"foo":["bar","qux"]}""");
    }

    [Fact]
    public void Removes_a_member_and_an_element()
    {
        Same(Apply("""{"baz":"qux","foo":"bar"}""", """[{"op":"remove","path":"/baz"}]"""), """{"foo":"bar"}""");
        Same(Apply("""{"foo":["bar","qux","baz"]}""", """[{"op":"remove","path":"/foo/1"}]"""), """{"foo":["bar","baz"]}""");
    }

    [Fact]
    public void Replaces_a_value_and_the_whole_document()
    {
        Same(Apply("""{"baz":"qux","foo":"bar"}""", """[{"op":"replace","path":"/baz","value":"boo"}]"""), """{"baz":"boo","foo":"bar"}""");
        Same(Apply("""{"a":1}""", """[{"op":"replace","path":"","value":{"b":2}}]"""), """{"b":2}""");
    }

    [Fact]
    public void Moves_and_copies()
    {
        Same(Apply("""{"foo":{"bar":"baz","waldo":"fred"},"qux":{"corge":"grault"}}""", """[{"op":"move","from":"/foo/waldo","path":"/qux/thud"}]"""), """{"foo":{"bar":"baz"},"qux":{"corge":"grault","thud":"fred"}}""");
        Same(Apply("""{"foo":["all","grass","cows","eat"]}""", """[{"op":"move","from":"/foo/1","path":"/foo/3"}]"""), """{"foo":["all","cows","eat","grass"]}""");
        Same(Apply("""{"a":{"b":1}}""", """[{"op":"copy","from":"/a/b","path":"/c"}]"""), """{"a":{"b":1},"c":1}""");
    }

    [Fact]
    public void Tests_a_value()
    {
        Same(Apply("""{"baz":"qux","foo":["a",2,"c"]}""", """[{"op":"test","path":"/baz","value":"qux"},{"op":"test","path":"/foo/1","value":2}]"""), """{"baz":"qux","foo":["a",2,"c"]}""");
        var failing = () => Apply("""{"baz":"qux"}""", """[{"op":"test","path":"/baz","value":"bar"}]""");
        failing.Should().Throw<JsonPatchException>().Which.Index.Should().Be(0);
    }

    [Fact]
    public void Unescapes_the_tokens_of_a_pointer()
    {
        Same(Apply("""{"a/b":1,"m~n":2}""", """[{"op":"replace","path":"/a~1b","value":9},{"op":"replace","path":"/m~0n","value":8}]"""), """{"a/b":9,"m~n":8}""");
    }

    [Theory]
    [InlineData("""[{"op":"remove","path":"/missing"}]""")]
    [InlineData("""[{"op":"replace","path":"/missing","value":1}]""")]
    [InlineData("""[{"op":"add","path":"/a/b/c","value":1}]""")]
    [InlineData("""[{"op":"add","path":"/list/9","value":1}]""")]
    [InlineData("""[{"op":"add","path":"/list/01","value":1}]""")]
    [InlineData("""[{"op":"remove","path":"/list/2"}]""")]
    [InlineData("""[{"op":"move","from":"/a","path":"/a/x"}]""")]
    [InlineData("""[{"op":"copy","from":"/nothing","path":"/b"}]""")]
    [InlineData("""[{"op":"add","path":"a","value":1}]""")]
    [InlineData("""[{"op":"add","path":"/x"}]""")]
    [InlineData("""[{"op":"move","path":"/x"}]""")]
    [InlineData("""[{"op":"jump","path":"/x"}]""")]
    [InlineData("""[{"op":"add"}]""")]
    [InlineData("""[5]""")]
    [InlineData("""[{"op":"remove","path":""}]""")]
    public void Refuses_what_cannot_be_applied_and_names_the_operation(string patch)
    {
        var document = JsonNode.Parse("""{"a":{"b":1},"list":[1,2]}""")!;
        var before = document.ToJsonString();
        var failing = () => JsonPatch.Apply(document, (JsonArray)JsonNode.Parse(patch)!);
        failing.Should().Throw<JsonPatchException>().Which.Index.Should().Be(0);
        document.ToJsonString().Should().Be(before);
    }

    [Fact]
    public void A_failing_operation_changes_nothing_even_after_earlier_ones_worked()
    {
        var document = JsonNode.Parse("""{"a":1}""")!;
        var failing = () => JsonPatch.Apply(document, (JsonArray)JsonNode.Parse("""[{"op":"add","path":"/b","value":2},{"op":"remove","path":"/nope"}]""")!);
        failing.Should().Throw<JsonPatchException>().Which.Index.Should().Be(1);
        document.ToJsonString().Should().Be("""{"a":1}""");
    }
}

using System.Text.Json.Nodes;
using FluentAssertions;
using static Synapbiz.Brain.Authoring.Tests.Fixtures;

namespace Synapbiz.Brain.Authoring.Tests;

/// <summary>The catalog document, its reading and the check of a draft against it.</summary>
public sealed class CatalogTests
{
    [Fact]
    public void A_catalog_survives_a_round_trip()
    {
        var catalog = NoteCatalog() with
        {
            Events = [new CatalogName("practice.opened", "A practice opened.")],
            Stages = ["visit", "postvisit"],
        };
        var text = CatalogJson.Serialize(catalog);
        var parsed = CatalogJson.Parse(text);

        parsed.IsValid.Should().BeTrue(string.Join("; ", parsed.Errors.Select(error => error.Code + " " + error.Path)));
        parsed.Catalog!.Ports.Should().BeEquivalentTo(catalog.Ports);
        parsed.Catalog.Events.Should().BeEquivalentTo(catalog.Events);
        parsed.Catalog.Queues.Should().BeEquivalentTo(catalog.Queues);
        parsed.Catalog.Stages.Should().Equal(catalog.Stages!);
        parsed.Catalog.ContractDocuments.Should().HaveCount(2);
        CatalogJson.Serialize(parsed.Catalog).Should().Be(text, "the text is canonical");
    }

    [Fact]
    public void A_catalog_that_says_nothing_about_a_kind_of_thing_does_not_restrict_it()
    {
        var parsed = CatalogJson.Parse("""{"$spec":"brain.catalog@1","ports":[]}""");
        parsed.IsValid.Should().BeTrue();
        parsed.Catalog!.Events.Should().BeNull();
        parsed.Catalog.Queues.Should().BeNull();
        parsed.Catalog.Stages.Should().BeNull();
        parsed.Catalog.Ports.Should().BeEmpty();
        AuthoringCatalog.Unrestricted.Ports.Should().BeNull();
    }

    [Theory]
    [InlineData("not json", CatalogJson.ErrorJsonInvalid)]
    [InlineData("[]", CatalogJson.ErrorJsonInvalid)]
    [InlineData("""{"$spec":"brain.catalog@2"}""", CatalogJson.ErrorUnknownFormat)]
    [InlineData("""{"ports":[]}""", CatalogJson.ErrorUnknownFormat)]
    [InlineData("""{"$spec":"brain.catalog@1","ports":{}}""", CatalogJson.ErrorMalformed)]
    [InlineData("""{"$spec":"brain.catalog@1","ports":[5]}""", CatalogJson.ErrorMalformed)]
    [InlineData("""{"$spec":"brain.catalog@1","ports":[{"name":"a","kind":"teleport"}]}""", CatalogJson.ErrorMalformed)]
    [InlineData("""{"$spec":"brain.catalog@1","ports":[{"name":"a","kind":"read","delivery":"sometimes"}]}""", CatalogJson.ErrorMalformed)]
    [InlineData("""{"$spec":"brain.catalog@1","ports":[{"kind":"read"}]}""", CatalogJson.ErrorMalformed)]
    [InlineData("""{"$spec":"brain.catalog@1","queues":"all"}""", CatalogJson.ErrorMalformed)]
    [InlineData("""{"$spec":"brain.catalog@1","stages":[1]}""", CatalogJson.ErrorMalformed)]
    [InlineData("""{"$spec":"brain.catalog@1","surprise":true}""", CatalogJson.ErrorMalformed)]
    [InlineData("""{"$spec":"brain.catalog@1","ports":[{"name":"a","kind":"read"},{"name":"a","kind":"read"}]}""", CatalogJson.ErrorDuplicate)]
    [InlineData("""{"$spec":"brain.catalog@1","queues":[{"name":"q"},{"name":"q"}]}""", CatalogJson.ErrorDuplicate)]
    [InlineData("""{"$spec":"brain.catalog@1","contracts":[{"nonsense":true}]}""", CatalogJson.ErrorContract)]
    public void A_catalog_that_is_not_valid_is_refused_with_a_code(string text, string code)
    {
        var parsed = CatalogJson.Parse(text);
        parsed.IsValid.Should().BeFalse();
        parsed.Catalog.Should().BeNull();
        parsed.Errors.Should().Contain(error => error.Code == code, text);
    }

    private static CatalogProblem Single(AuthoringCatalog catalog, string json) =>
        CatalogCheck.Draft(catalog, Obj(json)).Should().ContainSingle().Subject;

    [Fact]
    public void A_port_outside_the_catalog_is_named_with_the_ports_that_exist_of_that_kind()
    {
        var problem = Single(NoteCatalog(), """{"body":{"steps":{"s":{"kind":"read","port":"records.unknown"}}}}""");
        problem.Code.Should().Be(CatalogCheck.PortUnavailable);
        problem.Path.Should().Be("/body/steps/s/port");
        problem.Name.Should().Be("records.unknown");
        problem.Step.Should().Be("s");
        problem.Available.Should().Equal("records.note_status");
    }

    [Fact]
    public void A_port_used_as_the_wrong_kind_is_refused()
    {
        var problem = Single(NoteCatalog(), """{"body":{"steps":{"s":{"kind":"action","port":"records.note_status","delivery":"atMostOnce"}}}}""");
        problem.Code.Should().Be(CatalogCheck.PortKind);
        problem.Detail.Should().Be("read");
        problem.Available.Should().Equal("messages.send_reminder");
    }

    [Fact]
    public void A_delivery_the_port_does_not_offer_is_refused_and_either_accepts_both()
    {
        var catalog = NoteCatalog() with
        {
            Ports = [new CatalogPort("messages.once", "action", "atMostOnce", true, null, null, null), new CatalogPort("messages.any", "action", "either", true, null, null, null)],
        };
        var problem = Single(catalog, """{"body":{"steps":{"s":{"kind":"action","port":"messages.once","delivery":"atLeastOnce"}}}}""");
        problem.Code.Should().Be(CatalogCheck.DeliveryUnsupported);
        problem.Detail.Should().Be("atMostOnce");
        CatalogCheck.Draft(catalog, Obj("""{"body":{"steps":{"s":{"kind":"action","port":"messages.any","delivery":"atLeastOnce"}}}}""")).Should().BeEmpty();
    }

    [Fact]
    public void An_event_outside_the_catalog_is_refused_wherever_it_is_named()
    {
        var catalog = NoteCatalog() with { Events = [new CatalogName("practice.opened", null)] };
        var problems = CatalogCheck.Draft(catalog, Obj("""
            {"body":{"accepts":["practice.closed"],"triggers":[{"kind":"event","event":"practice.moved"}],"steps":{"s":{"kind":"wait","event":"practice.reopened"}}}}
            """));
        problems.Should().OnlyContain(problem => problem.Code == CatalogCheck.EventUnavailable);
        problems.Select(problem => problem.Path).Should().Contain(["/body/accepts/0", "/body/triggers/0/event"]);
        problems.Should().HaveCountGreaterThanOrEqualTo(3);
        problems.Should().OnlyContain(problem => problem.Available.Contains("practice.opened"));
    }

    [Fact]
    public void A_queue_outside_the_catalog_is_refused()
    {
        var problem = Single(NoteCatalog(), """{"body":{"steps":{"s":{"kind":"humanTask","queue":"somewhere_else"}}}}""");
        problem.Code.Should().Be(CatalogCheck.QueueUnavailable);
        problem.Available.Should().Equal("note_followup", "note_confirmation");
    }

    [Fact]
    public void A_stage_outside_the_catalog_is_refused()
    {
        var catalog = NoteCatalog() with { Stages = ["visit"] };
        var problem = Single(catalog, """{"body":{"stages":["visit","billing"]}}""");
        problem.Code.Should().Be(CatalogCheck.StageUnavailable);
        problem.Path.Should().Be("/body/stages/1");
        problem.Name.Should().Be("billing");
    }

    [Fact]
    public void An_unrestricted_catalog_refuses_nothing()
    {
        CatalogCheck.Draft(AuthoringCatalog.Unrestricted, Obj("""{"body":{"stages":["x"],"accepts":["e"],"steps":{"s":{"kind":"read","port":"p"},"t":{"kind":"humanTask","queue":"q"}}}}""")).Should().BeEmpty();
    }

    [Fact]
    public void A_step_id_with_a_slash_is_escaped_in_the_pointer()
    {
        var problem = Single(NoteCatalog(), """{"body":{"steps":{"a/b":{"kind":"read","port":"records.unknown"}}}}""");
        problem.Path.Should().Be("/body/steps/a~1b/port");
    }
}

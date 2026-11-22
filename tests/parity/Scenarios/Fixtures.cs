using Nuvora.Decisions.Contracts.Kinds;

namespace Synapbiz.Brain.Parity;

/// <summary>Shared kind descriptors for the parity scenarios.</summary>
internal static class Fixtures
{
    internal static readonly DateOnly AsOf = new(2026, 11, 18);

    internal static DecisionKindDescriptor Descriptor(
        string key, IReadOnlyList<FactDescriptor> facts, bool requiresTotalCoverage = false) => new()
    {
        Key = DecisionKindKey.From(key),
        SchemaVersion = 1,
        Facts = facts,
        OutcomeFields = [new OutcomeFieldDescriptor("class", FactType.Code), new OutcomeFieldDescriptor("score", FactType.Integer)],
        EvaluationMode = EvaluationMode.OnDemand,
        RequiresTotalCoverage = requiresTotalCoverage,
        ReadPermission = "parity.read",
        AuthorPermission = "parity.author",
        ApprovePermission = "parity.approve",
        ContainsPersonalData = false,
        ReadTracePermission = "parity.read_trace",
    };

    /// <summary>One fact per fact type; every fact except <c>code</c> is nullable.</summary>
    internal static DecisionKindDescriptor ParityKind() => Descriptor(
        "synthetic.parity",
        [
            new FactDescriptor("code", FactType.Code),
            new FactDescriptor("enm", FactType.Enum, Nullable: true, AllowedValues: ["A", "B", "C"]),
            new FactDescriptor("flag", FactType.Boolean, Nullable: true),
            new FactDescriptor("count", FactType.Integer, Nullable: true),
            new FactDescriptor("ratio", FactType.Decimal, Nullable: true),
            new FactDescriptor("amount", FactType.Money, Nullable: true),
            new FactDescriptor("day", FactType.Date, Nullable: true),
            new FactDescriptor("partner", FactType.Reference, Nullable: true),
        ]);

    internal static DecisionKindDescriptor ChannelKind(string key, params string[] allowedValues) => Descriptor(
        key, [new FactDescriptor("channel", FactType.Enum, AllowedValues: allowedValues)]);

    internal static DecisionKindDescriptor AmountKind(string key, FactType type) => Descriptor(
        key, [new FactDescriptor("amount", type)]);
}

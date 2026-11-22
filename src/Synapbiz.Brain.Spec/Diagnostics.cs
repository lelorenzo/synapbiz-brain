namespace Synapbiz.Brain.Spec;

/// <summary>The diagnostic codes and texts of the rule loader. Texts are data: the English and Spanish messages and the fix hint.</summary>
internal static class Diagnostics
{
    internal const string GoldenOutcomeMissing = "brain.rule.golden_outcome_missing";
    internal const string ContractUnavailable = "brain.contract.unavailable";
    internal const string OwningContractRequired = "brain.contract.module_required";
    internal const string KindNotRegistered = "decisions.kind.not_registered";
    internal const string DuplicateLegacyKey = "decisions.validation.duplicate_row_key";
    internal const string UnknownFact = "decisions.validation.unknown_fact";
    internal const string OperatorNotAllowed = "decisions.condition.operator_not_allowed";
    internal const string MixedCurrencies = "decisions.validation.money_currency_mismatch";
    internal const string RangeTypeMismatch = "decisions.condition.range_type_mismatch";
    internal const string CellTypeMismatch = "decisions.validation.cell_type_mismatch";
    internal const string LabelSourceMismatch = "brain.fact.label_source_mismatch";
    internal const string GoldenFactUnknown = "decisions.facts.missing";
    internal const string GoldenFactTypeMismatch = "decisions.facts.type_mismatch";
    internal const string GoldenRowUnknown = "brain.golden.unknown_row";
    internal const string MalformedDefinition = "decisions.definition.malformed";
    internal const string OutcomeFieldUnknown = "brain.outcome.unknown_field";
    internal const string OutcomeTypeMismatch = "brain.outcome.type_mismatch";
    internal const string OutcomeFieldRequired = "brain.outcome.required_field";
    internal const string EnumNotAllowed = "brain.fact.enum_not_allowed";

    internal const string StructureEnvelope = "spec.structure.envelope";
    internal const string StructureUnknownFormat = "spec.structure.unknown_format";
    internal const string StructureSchema = "spec.structure.schema";
    internal const string JsonInvalid = "spec.json.invalid";
    internal const string SemanticsNode = "spec.semantics.node";
    internal const string SemanticsDuplicateId = "spec.semantics.duplicate_id";
    internal const string SemanticsReference = "spec.semantics.reference";
    internal const string SemanticsCycle = "spec.semantics.cycle";
    internal const string SemanticsTranslation = "spec.semantics.translation";

    internal static BrainSpecError JsonNotValid() => new(JsonInvalid, "",
        "The input is not valid I-JSON.", "La entrada no es I-JSON válido.",
        "Use finite numbers, valid Unicode and unique member names.");

    internal static BrainSpecError Envelope() => new(StructureEnvelope, "",
        "A section needs a format and stable ID.", "La sección requiere un formato y un ID estable.",
        "Set $spec to domain.kind@major and supply a valid id.");

    internal static BrainSpecError UnknownFormat() => new(StructureUnknownFormat, "/$spec",
        "The format has no registered schema.", "El formato no tiene un esquema registrado.",
        "Register a schema for this format or use a registered major.");

    internal static BrainSpecError Schema(string path) => new(StructureSchema, path,
        "The value violates the format schema.", "El valor incumple el esquema del formato.",
        "Update this value to satisfy the format's JSON Schema.");

    internal static BrainSpecError Node(string path) => new(SemanticsNode, path,
        "A node needs a stable ID and positive version.", "El nodo requiere un ID estable y una versión positiva.",
        "Set id and a positive safe-integer version.");

    internal static BrainSpecError DuplicateId(string path) => new(SemanticsDuplicateId, path,
        "The node ID is duplicated.", "El ID del nodo está duplicado.",
        "Assign a distinct, stable ID to each node.");

    internal static BrainSpecError Translation(string path) => new(SemanticsTranslation, path,
        "A translation key or locale value is missing.", "Falta una clave de traducción o un valor de idioma.",
        "Supply a translation key and a string for each required locale.");

    internal static BrainSpecError Reference(string path) => new(SemanticsReference, path,
        "The section reference cannot be resolved.", "No se puede resolver la referencia de sección.",
        "Supply the referenced section or correct its address.");

    internal static BrainSpecError Cycle(string path) => new(SemanticsCycle, path,
        "The section reference creates a cycle.", "La referencia de sección crea un ciclo.",
        "Remove a reference in the cycle.");

    internal static BrainSpecError GoldenOutcomeRequired(string path) => new(GoldenOutcomeMissing, path,
        "A decided golden case requires an outcome.", "Un caso de prueba con decisión requiere un resultado.",
        "Supply the expected outcome for the selected row.");

    internal static BrainSpecError ContractNotAvailable(string path) => new(ContractUnavailable, path,
        "The required module contract version is unavailable.", "No está disponible la versión requerida del contrato del módulo.",
        "Supply the generated contract for this module and version.");

    internal static BrainSpecError OwnerRequired() => new(OwningContractRequired, "/requires",
        "The section must declare its owning module contract.", "La sección debe declarar el contrato del módulo propietario.",
        "Add the owning module and its contract version to requires.");

    internal static BrainSpecError KindUnknown(string path) => new(KindNotRegistered, path,
        "The module contract does not declare this decision kind.", "El contrato del módulo no declara este tipo de decisión.",
        "Use a decision kind declared by the required module contract.");

    internal static BrainSpecError RowKeyDuplicated(string path) => new(DuplicateLegacyKey, path,
        "The legacy row key is duplicated.", "La clave heredada de la fila está duplicada.",
        "Use a distinct legacy key for every row and the default row.");

    internal static BrainSpecError FactUnknown(string path) => new(UnknownFact, path,
        "The decision kind does not declare this fact.", "El tipo de decisión no declara este hecho.",
        "Use a fact declared by the decision kind.");

    internal static BrainSpecError OperatorUnsupported(string path) => new(OperatorNotAllowed, path,
        "The operator is not allowed for this fact type.", "El operador no se permite para este tipo de hecho.",
        "Choose an operator supported by the fact type.");

    internal static BrainSpecError CurrenciesMixed(string path) => new(MixedCurrencies, path,
        "The condition uses more than one currency.", "La condición usa más de una moneda.",
        "Use one currency per Money condition.");

    internal static BrainSpecError RangeBoundMismatch(string path) => new(RangeTypeMismatch, path,
        "The range bound does not match the fact type.", "El límite del intervalo no coincide con el tipo de hecho.",
        "Supply a range bound of the declared fact type.");

    internal static BrainSpecError OperandMismatch(string path) => new(CellTypeMismatch, path,
        "The condition operand is missing or has the wrong type.", "El operando de la condición falta o tiene un tipo incorrecto.",
        "Supply an operand of the declared fact type.");

    internal static BrainSpecError LabelSourceDiffers(string path) => new(LabelSourceMismatch, path,
        "The reference label source does not match the contract.", "El origen de etiquetas de la referencia no coincide con el contrato.",
        "Use the reference label source declared by the module contract.");

    internal static BrainSpecError GoldenFactUndeclared(string path) => new(GoldenFactUnknown, path,
        "The decision kind does not declare this golden fact.", "El tipo de decisión no declara este hecho del caso de prueba.",
        "Use a fact declared by the decision kind.");

    internal static BrainSpecError GoldenFactMismatch(string path) => new(GoldenFactTypeMismatch, path,
        "The golden fact value does not match the declared type.", "El valor del hecho del caso de prueba no coincide con el tipo declarado.",
        "Supply a value of the type declared for this fact.");

    internal static BrainSpecError GoldenRowUndeclared(string path) => new(GoldenRowUnknown, path,
        "The expected row ID does not exist in the rule.", "El ID de fila esperado no existe en la regla.",
        "Use the stable ID of a row in this rule, including its default row.");

    internal static BrainSpecError GoldenOutcomeShape(string path) => new(MalformedDefinition, path,
        "The golden outcome shape does not match the hit policy.", "La forma del resultado esperado no coincide con la política de coincidencia.",
        "Use an array of outcomes for Collect, or one outcome object for Unique or First.");

    internal static BrainSpecError OutcomeFieldUndeclared(string path) => new(OutcomeFieldUnknown, path,
        "The decision kind does not declare this outcome field.", "El tipo de decisión no declara este campo de resultado.",
        "Use an outcome field declared by the decision kind.");

    internal static BrainSpecError OutcomeValueMismatch(string path) => new(OutcomeTypeMismatch, path,
        "The outcome field value does not match its declared type.", "El valor del campo de resultado no coincide con el tipo declarado.",
        "Supply a value of the declared type or null for a nullable field.");

    internal static BrainSpecError OutcomeFieldMissing(string path) => new(OutcomeFieldRequired, path,
        "A required outcome field is missing.", "Falta un campo de resultado obligatorio.",
        "Supply the required field and its declared value type.");

    internal static BrainSpecError EnumValueNotAllowed(string path) => new(EnumNotAllowed, path,
        "The value is outside the contract's enumeration.", "El valor no pertenece a la enumeración del contrato.",
        "Use a value listed in the module contract.");

    internal static BrainSpecError ProjectionMalformed(string path, string message) => new(MalformedDefinition, path,
        message, "El documento de decisión no tiene la estructura esperada.",
        "Supply a decision document that matches the expected wire format.");
}

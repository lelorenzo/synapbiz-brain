using Synapbiz.Brain.Spec;

namespace Synapbiz.Brain.Process;

/// <summary>
/// The diagnostic codes and texts of the process loader. The codes and texts of the JSON, envelope, unknown format and
/// schema errors are the ones <c>Synapbiz.Brain.Spec</c> uses; the others are specific to <c>brain.process@1</c>.
/// </summary>
internal static class Diagnostics
{
    internal const string StepUnknown = "process.step.unknown";
    internal const string StepUnreachable = "process.step.unreachable";
    internal const string StageUnknown = "process.stage.unknown";
    internal const string VariableUnknown = "process.variable.unknown";
    internal const string VariableType = "process.variable.type";
    internal const string VariableInitial = "process.variable.initial";
    internal const string RuleUnavailable = "process.rule.unavailable";
    internal const string RuleFactUnknown = "process.rule.fact_unknown";
    internal const string RuleOutcomeUnknown = "process.rule.outcome_unknown";
    internal const string RuleHitPolicy = "process.rule.hit_policy";
    internal const string RouteIncomplete = "process.route.incomplete";
    internal const string RouteUnknownValue = "process.route.unknown_value";
    internal const string UsesMissing = "process.uses.missing";
    internal const string UsesUnused = "process.uses.unused";
    internal const string SubprocessUnavailable = "process.subprocess.unavailable";
    internal const string AiNextNotDecide = "process.ai.next_not_decide";
    internal const string AiSchemaInvalid = "process.ai.schema_invalid";
    internal const string CycleUnbounded = "process.cycle.unbounded";
    internal const string DurationInvalid = "process.duration.invalid";
    internal const string ScenarioInvalid = "process.scenario.invalid";
    internal const string ActionWindowRequired = "process.action.window_required";
    internal const string WindowInvalid = "process.window.invalid";
    internal const string WindowUnknown = "process.window.unknown";
    internal const string TriggerEventDuplicate = "process.trigger.event_duplicate";
    internal const string TriggerScheduleDuplicate = "process.trigger.schedule_duplicate";
    internal const string TriggerCronInvalid = "process.trigger.cron_invalid";
    internal const string TriggerZoneInvalid = "process.trigger.zone_invalid";
    internal const string TriggerInputSourceUnknown = "process.trigger.input_source_unknown";
    internal const string TriggerVariableUnknown = "process.trigger.variable_unknown";
    internal const string TriggerVariableNotInput = "process.trigger.variable_not_input";
    internal const string VariableTypeMismatch = "process.variable.type_mismatch";
    internal const string PollTooManyRounds = "process.poll.too_many_rounds";

    internal const string StructureEnvelope = "spec.structure.envelope";
    internal const string StructureUnknownFormat = "spec.structure.unknown_format";
    internal const string StructureSchema = "spec.structure.schema";
    internal const string JsonInvalid = "spec.json.invalid";

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

    internal static BrainSpecError UnknownStep(string path) => new(StepUnknown, path,
        "The step reference names no step of the process.", "La referencia de paso no nombra ningún paso del proceso.",
        "Name a step declared in body.steps.");

    internal static BrainSpecError UnreachableStep(string path) => new(StepUnreachable, path,
        "The step cannot be reached from the start step.", "El paso no es alcanzable desde el paso inicial.",
        "Route to this step from a reachable step, or remove it.");

    internal static BrainSpecError UnknownStage(string path) => new(StageUnknown, path,
        "The stage is not listed in body.stages.", "La etapa no figura en body.stages.",
        "List the stage in body.stages or use a listed stage.");

    internal static BrainSpecError UnknownVariable(string path) => new(VariableUnknown, path,
        "The variable is not declared in body.variables.", "La variable no está declarada en body.variables.",
        "Declare the variable in body.variables or use a declared variable.");

    internal static BrainSpecError WrongVariableType(string path) => new(VariableType, path,
        "The variable has a type that is not allowed here.", "La variable tiene un tipo que no se permite aquí.",
        "Use a variable of the type this member requires.");

    internal static BrainSpecError BadInitial(string path) => new(VariableInitial, path,
        "The initial value or the allowed values do not fit the variable type.",
        "El valor inicial o los valores permitidos no corresponden al tipo de la variable.",
        "Supply a value of the declared type; allowedValues belong to Enum variables only and must not be empty.");

    internal static BrainSpecError UnavailableRule(string path) => new(RuleUnavailable, path,
        "The rule is not among the rules of the load context.", "La regla no está entre las reglas del contexto de carga.",
        "Supply the rule in the load context or correct the rule id.");

    internal static BrainSpecError UnknownFact(string path) => new(RuleFactUnknown, path,
        "The rule's decision kind does not declare this fact.", "El tipo de decisión de la regla no declara este hecho.",
        "Use a fact declared by the decision kind of the rule.");

    internal static BrainSpecError UnknownOutcome(string path) => new(RuleOutcomeUnknown, path,
        "The name is not a usable outcome field of the rule's decision kind.",
        "El nombre no es un campo de resultado utilizable del tipo de decisión de la regla.",
        "Use an outcome field declared by the decision kind; a route field must be of type Enum, Boolean or Code.");

    internal static BrainSpecError WrongHitPolicy(string path) => new(RuleHitPolicy, path,
        "The members of the step do not match the hit policy of the rule.",
        "Los miembros del paso no corresponden a la política de coincidencia de la regla.",
        "Use output with next or route and on for Unique and First rules, and collectInto with next for Collect rules.");

    internal static BrainSpecError IncompleteRoute(string path) => new(RouteIncomplete, path,
        "The routing table does not cover every possible value.", "La tabla de rutas no cubre todos los valores posibles.",
        "Add a step reference for every missing value.");

    internal static BrainSpecError UnknownRouteValue(string path) => new(RouteUnknownValue, path,
        "The routing key is not a possible value.", "La clave de la ruta no es un valor posible.",
        "Use only values the routed field can take.");

    internal static BrainSpecError MissingUse(string path) => new(UsesMissing, path,
        "The rule or process is not listed in uses.", "La regla o el proceso no figura en uses.",
        "Add a { \"$ref\": \"rule/<id>\" } or { \"$ref\": \"process/<id>\" } entry to uses.");

    internal static BrainSpecError UnusedUse(string path) => new(UsesUnused, path,
        "No step names the referenced rule or process.", "Ningún paso nombra la regla o el proceso referenciado.",
        "Remove the entry from uses or add the step that needs it.");

    internal static BrainSpecError UnavailableSubprocess(string path) => new(SubprocessUnavailable, path,
        "The process is not among the known process ids.", "El proceso no está entre los ids de proceso conocidos.",
        "Supply the process id in the load context or correct the process id.");

    internal static BrainSpecError AiNextNotDecision(string path) => new(AiNextNotDecide, path,
        "The next step of an ai step must be a decide step.", "El paso siguiente de un paso ai debe ser un paso decide.",
        "Route the ai step to a decide step that validates its output.");

    internal static BrainSpecError AiSchemaNotValid(string path) => new(AiSchemaInvalid, path,
        "The value is not a valid JSON Schema.", "El valor no es un JSON Schema válido.",
        "Supply a JSON Schema (draft 2020-12) object.");

    internal static BrainSpecError UnboundedCycle(string path) => new(CycleUnbounded, path,
        "The step is on a cycle that contains no loop step.", "El paso está en un ciclo que no contiene ningún paso loop.",
        "Route the cycle through a loop step so that it is bounded.");

    internal static BrainSpecError BadDuration(string path) => new(DurationInvalid, path,
        "The duration is empty, zero or longer than 3660 days.", "La duración está vacía, es cero o supera 3660 días.",
        "Use an ISO 8601 duration of at least one second and at most 3660 days.");

    internal static BrainSpecError BadScenario(string path) => new(ScenarioInvalid, path,
        "The scenario refers to something the process does not declare or is malformed.",
        "El escenario se refiere a algo que el proceso no declara o está mal formado.",
        "Use step ids, variables and events declared by the process.");

    internal static BrainSpecError WindowRequired(string path) => new(ActionWindowRequired, path,
        "A contact action must name a delivery window.", "Una acción de contacto debe nombrar una ventana de entrega.",
        "Add window with the name of a window declared in body.windows, or remove contact.");

    internal static BrainSpecError BadWindow(string path) => new(WindowInvalid, path,
        "The delivery window is not valid.", "La ventana de entrega no es válida.",
        "Use an IANA zone id or { \"variable\": ... }, one or more distinct days (mon to sun), and HH:mm times with from before to (to may be 24:00).");

    internal static BrainSpecError UnknownWindow(string path) => new(WindowUnknown, path,
        "The window is not declared in body.windows.", "La ventana no está declarada en body.windows.",
        "Declare the window in body.windows or use a declared window.");

    internal static BrainSpecError EventDuplicate(string path) => new(TriggerEventDuplicate, path,
        "Another event trigger already names this event.", "Otro disparador de evento ya nombra este evento.",
        "Use each event name in one event trigger only.");

    internal static BrainSpecError ScheduleDuplicate(string path) => new(TriggerScheduleDuplicate, path,
        "Another schedule trigger already has this id.", "Otro disparador de agenda ya tiene este id.",
        "Give every schedule trigger its own id.");

    internal static BrainSpecError CronInvalid(string path) => new(TriggerCronInvalid, path,
        "The cron expression is not valid or can never fire.", "La expresión cron no es válida o nunca se dispara.",
        "Use five fields (minute hour day-of-month month day-of-week) of *, numbers, ranges, steps and lists; no names, seconds or macros.");

    internal static BrainSpecError ZoneInvalid(string path) => new(TriggerZoneInvalid, path,
        "The time zone is not an IANA zone id.", "La zona horaria no es un id de zona IANA.",
        "Use UTC or an id such as America/New_York.");

    internal static BrainSpecError InputSourceUnknown(string path) => new(TriggerInputSourceUnknown, path,
        "A schedule trigger can only map scheduledFor or scheduledDate.", "Un disparador de agenda solo puede asignar scheduledFor o scheduledDate.",
        "Map the variable to scheduledFor (an Instant variable) or scheduledDate (a Date variable).");

    internal static BrainSpecError TriggerVariableUnknownAt(string path) => new(TriggerVariableUnknown, path,
        "The trigger maps a variable that is not declared in body.variables.", "El disparador asigna una variable que no está declarada en body.variables.",
        "Declare the variable in body.variables or map a declared variable.");

    internal static BrainSpecError TriggerVariableNotInputAt(string path) => new(TriggerVariableNotInput, path,
        "The trigger maps a variable that is not an input variable.", "El disparador asigna una variable que no es de entrada.",
        "Declare the variable with input: true.");

    internal static BrainSpecError TypeMismatch(string path) => new(VariableTypeMismatch, path,
        "The variable does not have the type the trigger source produces.", "La variable no tiene el tipo que produce el origen del disparador.",
        "Use an Instant variable for scheduledFor and a Date variable for scheduledDate.");

    internal static BrainSpecError TooManyRounds(string path) => new(PollTooManyRounds, path,
        "More than 10000 rounds fit in maxDuration.", "En maxDuration caben más de 10000 rondas.",
        "Lengthen every, use exponential backoff, or shorten maxDuration.");
}

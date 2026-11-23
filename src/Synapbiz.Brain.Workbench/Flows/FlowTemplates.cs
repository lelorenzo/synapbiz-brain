using System.Text.RegularExpressions;

namespace Synapbiz.Brain.Workbench.Flows;

/// <summary>
/// Every sentence the flow surface writes, in English and Spanish, as templates with <c>{name}</c> placeholders. A text is never built from
/// free text of a document: the document only supplies ids and labels, which fill the placeholders.
/// </summary>
internal static partial class FlowTemplates
{
    /// <summary>The keys whose Spanish text is, on purpose, the English text: punctuation and separators.</summary>
    internal static readonly IReadOnlySet<string> IntentionallyEqual = new HashSet<string>(StringComparer.Ordinal)
    {
        "join.sep",
    };

    private static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
    {
        ["join.sep"] = ", ",
        ["join.and"] = " and ",
        ["join.or"] = " or ",
        ["duration.day.one"] = "1 day",
        ["duration.day.many"] = "{n} days",
        ["duration.hour.one"] = "1 hour",
        ["duration.hour.many"] = "{n} hours",
        ["duration.minute.one"] = "1 minute",
        ["duration.minute.many"] = "{n} minutes",
        ["duration.second.one"] = "1 second",
        ["duration.second.many"] = "{n} seconds",
        ["when.within"] = "within {duration}",
        ["when.before"] = "{duration} before `{variable}`",
        ["when.after"] = "{duration} after `{variable}`",
        ["when.at"] = "by `{variable}`",
        ["time.at.one"] = "at {time}",
        ["time.at.many"] = "at {time}",
        ["kind.read"] = "Read",
        ["kind.action"] = "Action",
        ["kind.ai"] = "AI answer",
        ["kind.poll"] = "Repeated read",
        ["kind.decide"] = "Decision",
        ["kind.humanTask"] = "Task for a person",
        ["kind.wait"] = "Wait for an event",
        ["kind.timer"] = "Timer",
        ["kind.loop"] = "Repetition",
        ["kind.subprocess"] = "Child process",
        ["kind.end"] = "End",
        ["step.read"] = "Reads information from `{port}`.",
        ["step.action"] = "Asks `{port}` to do something.",
        ["step.action.contact"] = "It contacts a person.",
        ["step.action.atMostOnce"] = "It is never sent twice.",
        ["step.action.window"] = "It only goes out inside the delivery window {window}.",
        ["step.action.interrupt"] = "If `{event}` arrives while it waits for the window, the wait ends.",
        ["step.ai"] = "Asks an AI model (`{model}`, prompt `{prompt}` version {version}) through `{port}`.",
        ["step.poll"] = "Reads `{port}` again and again until `{field}` is {values}.",
        ["step.poll.fixed"] = "It reads every {every}, for at most {total}.",
        ["step.poll.exponential"] = "It reads after {every} and doubles the wait each time, for at most {total}.",
        ["step.poll.cap"] = " The wait never grows beyond {cap}.",
        ["step.sends"] = "It sends {names}.",
        ["step.keeps"] = "It keeps {names}.",
        ["step.retry"] = "If the call fails for a passing reason it tries again, up to {count} attempts in all.",
        ["step.decide"] = "Judges with the rule `{rule}` using {facts}.",
        ["step.decide.nofacts"] = "Judges with the rule `{rule}`.",
        ["step.decide.collect"] = "Every matching result is kept in `{variable}`.",
        ["step.decide.route"] = "The result field `{field}` says where the flow goes.",
        ["step.task"] = "Gives a task to the queue {queue}. A person chooses {outcomes}.",
        ["step.task.show"] = "The person sees {names}.",
        ["step.wait"] = "Waits for the event `{event}`.",
        ["step.wait.correlation"] = "Only the event that carries the same `{variable}` counts.",
        ["step.timer.after"] = "Waits {duration}.",
        ["step.timer.until"] = "Waits until {when}.",
        ["step.timer.at"] = "Waits until `{variable}`.",
        ["step.loop"] = "Repeats up to {count} times.",
        ["step.loop.each"] = "Goes through `{list}` one item at a time, up to {count} items; the current item is in `{item}`.",
        ["step.subprocess"] = "Starts the process `{process}` and waits until it ends.",
        ["step.end"] = "Ends the case with the result {result}.",
        ["ai.pinned"] = "The model and the prompt are fixed in the document: `{model}`, prompt `{prompt}` version {version}.",
        ["ai.attempts.one"] = "It gets 1 attempt to give an answer that fits the expected form.",
        ["ai.attempts.many"] = "It gets {count} attempts to give an answer that fits the expected form.",
        ["ai.fallback"] = "When the answer never fits, the call fails or the AI is not available, the flow does not stop: it goes on at {targets}.",
        ["ai.judged"] = "The rule `{rule}` judges the answer next.",
        ["route.next.read"] = "When it succeeds: go to {target}",
        ["route.next.action"] = "When it succeeds: go to {target}",
        ["route.next.ai"] = "When an answer fits: go to {target}",
        ["route.next.poll"] = "When the answer says it is done: go to {target}",
        ["route.next.wait"] = "When the event arrives: go to {target}",
        ["route.next.timer"] = "When the time comes: go to {target}",
        ["route.next.loop"] = "When every item has been handled: go to {target}",
        ["route.next.subprocess"] = "When the process ends: go to {target}",
        ["route.next.decide"] = "Then: go to {target}",
        ["route.body"] = "For each round: go to {target}",
        ["route.onError"] = "If it fails: go to {target}",
        ["route.onError.decide"] = "If the rule cannot be applied: go to {target}",
        ["route.onError.subprocess"] = "If the process fails: go to {target}",
        ["route.onNotAvailable"] = "If the connection is not available: go to {target}",
        ["route.onNotEntitled"] = "If the plan does not include it: go to {target}",
        ["route.onUnknown"] = "If it is not known whether it went out: go to {target}",
        ["route.onTimeout"] = "If no answer arrives {when}: go to {target}",
        ["route.onExhausted"] = "If the rounds run out: go to {target}",
        ["route.onNoDecision"] = "If the rule gives no result: go to {target}",
        ["route.onInvalid"] = "If the answer does not fit the expected form: go to {target}",
        ["route.onExpired"] = "If the time runs out: go to {target}",
        ["route.on.decide"] = "If `{field}` is {value}: go to {target}",
        ["route.on.humanTask"] = "If the person chooses {value}: go to {target}",
        ["route.on.subprocess"] = "If the process ends with {value}: go to {target}",
        ["route.interrupt"] = "If `{event}` arrives while it waits for the window: go to {target}",
        ["trigger.event.key"] = "Starts when `{event}` arrives, one case for each `{key}`.",
        ["trigger.schedule.daily"] = "Runs every day {at} {zone}.",
        ["trigger.schedule.weekdays"] = "Runs every weekday {at} {zone}.",
        ["trigger.schedule.days"] = "Runs every {days} {at} {zone}.",
        ["trigger.schedule.cron"] = "Runs on the schedule `{cron}` {zone}.",
        ["trigger.zone.activation"] = "in the zone of the activation",
        ["day.0"] = "Sunday",
        ["day.1"] = "Monday",
        ["day.2"] = "Tuesday",
        ["day.3"] = "Wednesday",
        ["day.4"] = "Thursday",
        ["day.5"] = "Friday",
        ["day.6"] = "Saturday",
        ["error.route_unexplained"] = "The history of step `{step}` does not say which way the case left it: it went to `{to}`.",
        ["review.stale"] = "The review was written for another version of this flow (its content hash is not the hash of the document).",
        ["review.target_unknown"] = "The flow has nothing at `{target}`.",
        ["review.scenario_invalid"] = "The proposed scenario is not valid for this flow: {code} at {path}.",
        ["review.scenario_not_object"] = "The proposed scenario is not a JSON object.",
    };

    private static readonly Dictionary<string, string> Spanish = new(StringComparer.Ordinal)
    {
        ["join.sep"] = ", ",
        ["join.and"] = " y ",
        ["join.or"] = " o ",
        ["duration.day.one"] = "1 día",
        ["duration.day.many"] = "{n} días",
        ["duration.hour.one"] = "1 hora",
        ["duration.hour.many"] = "{n} horas",
        ["duration.minute.one"] = "1 minuto",
        ["duration.minute.many"] = "{n} minutos",
        ["duration.second.one"] = "1 segundo",
        ["duration.second.many"] = "{n} segundos",
        ["when.within"] = "en {duration}",
        ["when.before"] = "{duration} antes de `{variable}`",
        ["when.after"] = "{duration} después de `{variable}`",
        ["when.at"] = "antes de `{variable}`",
        ["time.at.one"] = "a la {time}",
        ["time.at.many"] = "a las {time}",
        ["kind.read"] = "Lectura",
        ["kind.action"] = "Acción",
        ["kind.ai"] = "Respuesta de IA",
        ["kind.poll"] = "Lectura repetida",
        ["kind.decide"] = "Decisión",
        ["kind.humanTask"] = "Tarea para una persona",
        ["kind.wait"] = "Espera de un evento",
        ["kind.timer"] = "Temporizador",
        ["kind.loop"] = "Repetición",
        ["kind.subprocess"] = "Proceso hijo",
        ["kind.end"] = "Fin",
        ["step.read"] = "Lee información de `{port}`.",
        ["step.action"] = "Pide a `{port}` que haga algo.",
        ["step.action.contact"] = "Contacta a una persona.",
        ["step.action.atMostOnce"] = "Nunca se envía dos veces.",
        ["step.action.window"] = "Solo sale dentro de la ventana de envío {window}.",
        ["step.action.interrupt"] = "Si llega `{event}` mientras espera la ventana, la espera termina.",
        ["step.ai"] = "Consulta a un modelo de IA (`{model}`, instrucción `{prompt}` versión {version}) mediante `{port}`.",
        ["step.poll"] = "Lee `{port}` una y otra vez hasta que `{field}` sea {values}.",
        ["step.poll.fixed"] = "Lee cada {every}, durante {total} como máximo.",
        ["step.poll.exponential"] = "Lee tras {every} y duplica la espera cada vez, durante {total} como máximo.",
        ["step.poll.cap"] = " La espera nunca supera {cap}.",
        ["step.sends"] = "Envía {names}.",
        ["step.keeps"] = "Guarda {names}.",
        ["step.retry"] = "Si la llamada falla por un motivo pasajero, lo intenta de nuevo, hasta {count} intentos en total.",
        ["step.decide"] = "Decide con la regla `{rule}` usando {facts}.",
        ["step.decide.nofacts"] = "Decide con la regla `{rule}`.",
        ["step.decide.collect"] = "Cada resultado que coincide se guarda en `{variable}`.",
        ["step.decide.route"] = "El campo de resultado `{field}` indica adónde va el flujo.",
        ["step.task"] = "Entrega una tarea a la cola {queue}. Una persona elige {outcomes}.",
        ["step.task.show"] = "La persona ve {names}.",
        ["step.wait"] = "Espera el evento `{event}`.",
        ["step.wait.correlation"] = "Solo cuenta el evento que trae el mismo `{variable}`.",
        ["step.timer.after"] = "Espera {duration}.",
        ["step.timer.until"] = "Espera hasta {when}.",
        ["step.timer.at"] = "Espera hasta `{variable}`.",
        ["step.loop"] = "Se repite hasta {count} veces.",
        ["step.loop.each"] = "Recorre `{list}` elemento por elemento, hasta {count} elementos; el elemento actual está en `{item}`.",
        ["step.subprocess"] = "Inicia el proceso `{process}` y espera a que termine.",
        ["step.end"] = "Cierra el caso con el resultado {result}.",
        ["ai.pinned"] = "El modelo y la instrucción están fijados en el documento: `{model}`, instrucción `{prompt}` versión {version}.",
        ["ai.attempts.one"] = "Tiene 1 intento para dar una respuesta que cumpla la forma esperada.",
        ["ai.attempts.many"] = "Tiene {count} intentos para dar una respuesta que cumpla la forma esperada.",
        ["ai.fallback"] = "Cuando la respuesta nunca cumple la forma, la llamada falla o la IA no está disponible, el flujo no se detiene: sigue en {targets}.",
        ["ai.judged"] = "A continuación la regla `{rule}` juzga la respuesta.",
        ["route.next.read"] = "Si sale bien: ir a {target}",
        ["route.next.action"] = "Si sale bien: ir a {target}",
        ["route.next.ai"] = "Cuando una respuesta cumple la forma: ir a {target}",
        ["route.next.poll"] = "Cuando la respuesta indica que terminó: ir a {target}",
        ["route.next.wait"] = "Cuando llega el evento: ir a {target}",
        ["route.next.timer"] = "Cuando llega la hora: ir a {target}",
        ["route.next.loop"] = "Cuando se han tratado todos los elementos: ir a {target}",
        ["route.next.subprocess"] = "Cuando el proceso termina: ir a {target}",
        ["route.next.decide"] = "Después: ir a {target}",
        ["route.body"] = "En cada vuelta: ir a {target}",
        ["route.onError"] = "Si falla: ir a {target}",
        ["route.onError.decide"] = "Si no se puede aplicar la regla: ir a {target}",
        ["route.onError.subprocess"] = "Si el proceso falla: ir a {target}",
        ["route.onNotAvailable"] = "Si la conexión no está disponible: ir a {target}",
        ["route.onNotEntitled"] = "Si el plan no lo incluye: ir a {target}",
        ["route.onUnknown"] = "Si no se sabe si salió: ir a {target}",
        ["route.onTimeout"] = "Si no llega respuesta {when}: ir a {target}",
        ["route.onExhausted"] = "Si se agotan las vueltas: ir a {target}",
        ["route.onNoDecision"] = "Si la regla no da resultado: ir a {target}",
        ["route.onInvalid"] = "Si la respuesta no cumple la forma esperada: ir a {target}",
        ["route.onExpired"] = "Si se acaba el tiempo: ir a {target}",
        ["route.on.decide"] = "Si `{field}` es {value}: ir a {target}",
        ["route.on.humanTask"] = "Si la persona elige {value}: ir a {target}",
        ["route.on.subprocess"] = "Si el proceso termina con {value}: ir a {target}",
        ["route.interrupt"] = "Si llega `{event}` mientras espera la ventana: ir a {target}",
        ["trigger.event.key"] = "Empieza cuando llega `{event}`, un caso por cada `{key}`.",
        ["trigger.schedule.daily"] = "Se ejecuta todos los días {at} {zone}.",
        ["trigger.schedule.weekdays"] = "Se ejecuta todos los días laborables {at} {zone}.",
        ["trigger.schedule.days"] = "Se ejecuta cada {days} {at} {zone}.",
        ["trigger.schedule.cron"] = "Se ejecuta según el horario `{cron}` {zone}.",
        ["trigger.zone.activation"] = "en la zona de la activación",
        ["day.0"] = "domingo",
        ["day.1"] = "lunes",
        ["day.2"] = "martes",
        ["day.3"] = "miércoles",
        ["day.4"] = "jueves",
        ["day.5"] = "viernes",
        ["day.6"] = "sábado",
        ["error.route_unexplained"] = "El historial del paso `{step}` no dice por dónde salió el caso: fue a `{to}`.",
        ["review.stale"] = "La revisión se escribió para otra versión de este flujo (su hash de contenido no es el del documento).",
        ["review.target_unknown"] = "El flujo no tiene nada en `{target}`.",
        ["review.scenario_invalid"] = "El escenario propuesto no es válido para este flujo: {code} en {path}.",
        ["review.scenario_not_object"] = "El escenario propuesto no es un objeto JSON.",
    };

    /// <summary>Every key of the English table.</summary>
    internal static IReadOnlyCollection<string> EnglishKeys => English.Keys;

    /// <summary>Every key of the Spanish table.</summary>
    internal static IReadOnlyCollection<string> SpanishKeys => Spanish.Keys;

    /// <summary>The template of a key as written, placeholders included.</summary>
    internal static string Template(WorkbenchLocale locale, string key) => Table(locale)[key];

    /// <summary>True when the table of the locale has the key.</summary>
    internal static bool Has(WorkbenchLocale locale, string key) => Table(locale).ContainsKey(key);

    /// <summary>The text of a key with its placeholders filled; a placeholder with no value is left as written.</summary>
    internal static string Text(WorkbenchLocale locale, string key, params (string Name, string Value)[] parameters)
    {
        var template = Table(locale)[key];
        if (parameters.Length == 0)
        {
            return template;
        }

        var values = parameters.ToDictionary(parameter => parameter.Name, parameter => parameter.Value, StringComparer.Ordinal);
        return Placeholder().Replace(template, match => values.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);
    }

    private static Dictionary<string, string> Table(WorkbenchLocale locale) => locale switch
    {
        WorkbenchLocale.En => English,
        WorkbenchLocale.Es => Spanish,
        _ => throw new ArgumentOutOfRangeException(nameof(locale), locale, "Unknown locale."),
    };

    [GeneratedRegex(@"\{(\w+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
}

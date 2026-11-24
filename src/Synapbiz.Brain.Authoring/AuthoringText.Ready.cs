namespace Synapbiz.Brain.Authoring;

internal static partial class AuthoringText
{
    private static void AddReady()
    {
        T("ready.requirement.authoring.ready.loader_errors", "Brain accepts the document: it has no errors.", "Brain acepta el documento: no tiene errores.");
        T("ready.requirement.authoring.ready.scenario_failing", "Every test of the process passes.", "Todas las pruebas del proceso pasan.");
        T("ready.requirement.authoring.ready.label_missing", "Every step and every stage has a name in English and in Spanish.", "Cada paso y cada etapa tiene un nombre en inglés y en español.");
        T("ready.requirement.authoring.ready.route_untested", "Every route is taken by at least one test, or the draft says why it is not tested.", "Cada ruta la recorre al menos una prueba, o el borrador dice por qué no se prueba.");
        T("ready.requirement.authoring.ready.contact_unsafe", "Every step that contacts a person goes out at most once, only inside a delivery window, and has a way out when it fails or when it is not known whether it went out.", "Cada paso que contacta a una persona sale como máximo una vez, solo dentro de una ventana de envío, y tiene salida cuando falla o cuando no se sabe si salió.");
        T("ready.requirement.authoring.ready.ai_without_fallback", "Every AI step has a way to go on that needs no AI, whatever goes wrong.", "Cada paso de IA tiene una forma de seguir que no necesita IA, pase lo que pase.");
        T("ready.requirement.authoring.ready.wait_without_timeout", "Every wait has a time limit, or the draft says why not.", "Cada espera tiene un límite de tiempo, o el borrador dice por qué no.");
        T("ready.requirement.authoring.ready.outside_catalog", "Everything the process names (ports, events, queues, stages) is available here.", "Todo lo que el proceso nombra (puertos, avisos, colas, etapas) está disponible aquí.");
        T("path.in.document", "in the document", "en el documento");
        T("path.in.step", "in the step {step}", "en el paso {step}");
        T("path.member", ", {member}", ", {member}");
        T("path.route", "the route taken {when}", "la ruta que se toma {when}");
        T("where.case", "in the case", "en el caso");
        T("where.facts", "in the facts given", "en los datos dados");
        T("where.fact", "for {name}", "para {name}");
        T("where.test", "in the test", "en la prueba");
        T("where.test.event", "in step number {n} of the test", "en el paso número {n} de la prueba");
        T("where.test.zones", "in the time zones of the test", "en las zonas horarias de la prueba");
        T("where.test.start", "in the start of the test", "en el inicio de la prueba");
        T("where.test.expect", "in what the test expects", "en lo que la prueba espera");
        T("where.test.name", "in the name of the test", "en el nombre de la prueba");
        T("path.in.window", "in the delivery window {window}", "en la ventana de envío {window}");
        T("path.in.variable", "in the piece of information {variable}", "en el dato {variable}");
        T("path.in.test", "in the test {name}", "en la prueba {name}");
        T("path.in.testNumber", "in test number {n}", "en la prueba número {n}");
        T("path.in.part", "in the part of the document called {part}", "en la parte del documento llamada {part}");
        T("path.in.meta", "in the names and labels", "en los nombres y etiquetas");
        T("path.in.uses", "in the list of rules and processes it uses", "en la lista de reglas y procesos que usa");
        T("path.in.header", "in the header of the document", "en el encabezado del documento");
        T("gap.route", "at {step}, the route taken {route}, which no test takes", "en {step}, la ruta que se toma {route}, que ninguna prueba recorre");
        T("gap.wait", "at {step}, the wait without a time limit", "en {step}, la espera sin límite de tiempo");
        T("ready.none", "nothing", "nada");
        T("ready.finding.loader", "Brain does not accept the process yet. {sentence}", "Brain todavía no acepta el proceso. {sentence}");
        T("ready.finding.scenario", "The test {name} does not pass. {reason}", "La prueba {name} no pasa. {reason}");
        T("ready.finding.noscenarios", "The process has no tests, so nothing shows that it works. Add at least one test.", "El proceso no tiene pruebas, así que nada muestra que funcione. Agrega al menos una prueba.");
        T("ready.finding.scenarionames", "Two tests have the same name {name}; each test needs a name of its own.", "Dos pruebas tienen el mismo nombre {name}; cada prueba necesita su propio nombre.");
        T("ready.finding.nocatalog", "No catalog was given, so connectors and contact safety cannot be checked. The host must give the workspace a catalog with the connections the process uses.", "No se dio ningún catálogo, así que no se pueden comprobar las conexiones ni la seguridad del contacto. El anfitrión debe dar al espacio de trabajo un catálogo con las conexiones que usa el proceso.");
        T("ready.finding.version", "The version number must be greater than the current one ({current}); raise it and save again.", "El número de versión debe ser mayor que el actual ({current}); súbelo y guarda de nuevo.");
        T("ready.finding.stale", "The approved process changed while you were working; save the draft again on the current version.", "El proceso aprobado cambió mientras trabajabas; guarda de nuevo el borrador sobre la versión actual.");
        T("ready.finding.basemismatch", "The draft is based on the approved process {base} but is the process {id}; a proposal keeps the process it comes from. Make the draft again from {base}.", "El borrador parte del proceso aprobado {base} pero es el proceso {id}; una propuesta conserva el proceso del que viene. Haz el borrador de nuevo desde {base}.");
        T("ready.requirement.authoring.ready.no_scenarios", "The process has at least one test.", "El proceso tiene al menos una prueba.");
        T("ready.requirement.authoring.ready.scenario_names_duplicated", "Every test has a name of its own.", "Cada prueba tiene su propio nombre.");
        T("ready.requirement.authoring.ready.catalog_missing", "A catalog is given whenever the process reads, acts, repeats a read or asks an AI, so connectors and contact safety can be checked.", "Hay un catálogo siempre que el proceso lee, actúa, repite una lectura o consulta a una IA, para poder comprobar las conexiones y la seguridad del contacto.");
        T("ready.requirement.authoring.ready.version_not_newer", "The version number is greater than the one of the approved process.", "El número de versión es mayor que el del proceso aprobado.");
        T("ready.requirement.authoring.ready.base_stale", "The approved process has not changed since the draft was based on it.", "El proceso aprobado no ha cambiado desde que se basó el borrador en él.");
        T("ready.requirement.authoring.ready.base_mismatch", "The draft is the same process as the approved one it comes from.", "El borrador es el mismo proceso que el aprobado del que viene.");
        T("portkind.read", "reading information", "leer información");
        T("portkind.action", "doing things", "hacer cosas");
        T("portkind.ai", "AI answers", "respuestas de IA");
        T("delivery.either", "either way", "de cualquier forma");
        T("ready.finding.label.step", "The step {step} has no name in {language}.", "El paso {step} no tiene nombre en {language}.");
        T("ready.finding.label.result", "The result {name} has no name in {language}.", "El resultado {name} no tiene nombre en {language}.");
        T("ready.finding.label.queue", "The task queue {name} has no name in {language}.", "La cola de tareas {name} no tiene nombre en {language}.");
        T("ready.finding.label.outcome", "The choice {name} has no name in {language}.", "La opción {name} no tiene nombre en {language}.");
        T("ready.finding.label.stage", "The stage {stage} has no name in {language}.", "La etapa {stage} no tiene nombre en {language}.");
        T("ready.finding.untested", "No test goes through this route of the step {step}: {route}, it goes to {target}.", "Ninguna prueba recorre esta ruta del paso {step}: {route}, va a {target}.");
        T("ready.finding.contact.once", "The step {step} contacts a person but could be sent more than once; it must go out at most once.", "El paso {step} contacta a una persona pero podría enviarse más de una vez; debe salir como máximo una vez.");
        T("ready.finding.contact.window", "The step {step} contacts a person but does not wait for a delivery window.", "El paso {step} contacta a una persona pero no espera una ventana de envío.");
        T("ready.finding.contact.failure", "The step {step} contacts a person but has no way out when it fails.", "El paso {step} contacta a una persona pero no tiene salida cuando falla.");
        T("ready.finding.contact.unknown", "The step {step} contacts a person but has no way out when it is not known whether the message went out.", "El paso {step} contacta a una persona pero no tiene salida cuando no se sabe si el mensaje salió.");
        T("ready.finding.ai.missing", "The AI step {step} has no way to go on {route}.", "El paso de IA {step} no tiene cómo seguir {route}.");
        T("ready.finding.ai.needsAi", "The AI step {step} goes on {route} to a path that needs AI again; it must be able to reach an end without AI.", "El paso de IA {step} sigue {route} por un camino que vuelve a necesitar IA; debe poder llegar a un final sin IA.");
        T("ready.finding.wait", "The step {step} waits without a time limit and the draft does not say why.", "El paso {step} espera sin límite de tiempo y el borrador no dice por qué.");
        T("ready.finding.catalog.port", "The port {name} is not available here ({where}). Available: {available}.", "El puerto {name} no está disponible aquí ({where}). Disponibles: {available}.");
        T("ready.finding.catalog.event", "The event {name} is not available here ({where}). Available: {available}.", "El aviso {name} no está disponible aquí ({where}). Disponibles: {available}.");
        T("ready.finding.catalog.queue", "The queue {name} is not available here ({where}). Available: {available}.", "La cola {name} no está disponible aquí ({where}). Disponibles: {available}.");
        T("ready.finding.catalog.portkind", "The port {name} exists here but only for {detail}, which this step cannot use ({where}). Available for it: {available}.", "El puerto {name} existe aquí pero solo para {detail}, que este paso no puede usar ({where}). Disponibles para él: {available}.");
        T("ready.finding.catalog.delivery", "The port {name} supports {detail} delivery only ({where}).", "El puerto {name} solo admite el envío {detail} ({where}).");
        T("ready.finding.catalog.stage", "The stage {name} is not available here ({where}). Available: {available}.", "La etapa {name} no está disponible aquí ({where}). Disponibles: {available}.");
    }
}

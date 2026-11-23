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
        T("path.member", "({member})", "({member})");
        T("path.in.window", "in the delivery window {window}", "en la ventana de envío {window}");
        T("path.in.variable", "in the piece of information {variable}", "en el dato {variable}");
        T("path.in.test", "in the test {name}", "en la prueba {name}");
        T("path.in.testNumber", "in test number {n}", "en la prueba número {n}");
        T("path.in.part", "in the part of the document called {part}", "en la parte del documento llamada {part}");
        T("path.in.meta", "in the names and labels", "en los nombres y etiquetas");
        T("path.in.uses", "in the list of rules and processes it uses", "en la lista de reglas y procesos que usa");
        T("path.in.header", "in the header of the document", "en el encabezado del documento");
        T("ready.none", "nothing", "nada");
        T("ready.finding.loader", "Brain refuses the document {where}: {message}", "Brain rechaza el documento {where}: {message}");
        T("ready.finding.scenario", "The test {name} does not pass: {reason}", "La prueba {name} no pasa: {reason}");
        T("ready.finding.label.step", "The step {step} has no name in {language}.", "El paso {step} no tiene nombre en {language}.");
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
        T("ready.finding.catalog.portkind", "The port {name} exists here but as a {detail} port, which this step cannot use ({where}). Available for it: {available}.", "El puerto {name} existe aquí pero como puerto de tipo {detail}, que este paso no puede usar ({where}). Disponibles para él: {available}.");
        T("ready.finding.catalog.delivery", "The port {name} supports {detail} delivery only ({where}).", "El puerto {name} solo admite el envío {detail} ({where}).");
        T("ready.finding.catalog.stage", "The stage {name} is not available here ({where}). Available: {available}.", "La etapa {name} no está disponible aquí ({where}). Disponibles: {available}.");
    }
}

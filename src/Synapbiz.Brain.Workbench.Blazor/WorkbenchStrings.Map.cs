namespace Synapbiz.Brain.Workbench.Blazor;

public static partial class WorkbenchStrings
{
    /// <summary>The strings of the process map and of the list by kind, every key under the prefix <c>flow.</c>, in Spanish and English.</summary>
    private static (string Key, string Es, string En)[] MapTable() =>
    [
        ("flow.view.label", "Vistas", "Views"),
        ("flow.view.map", "Mapa", "Map"),
        ("flow.view.list", "Lista", "List"),

        ("flow.map.title", "Mapa de procesos", "Process map"),
        ("flow.map.note", "Calculado de los propios documentos. Elija una pieza para abrirla.", "Worked out from the documents themselves. Choose a piece to open it."),
        ("flow.map.count.main.one", "{n} proceso principal", "{n} main process"),
        ("flow.map.count.main.other", "{n} procesos principales", "{n} main processes"),
        ("flow.map.count.sub.one", "{n} subproceso", "{n} subprocess"),
        ("flow.map.count.sub.other", "{n} subprocesos", "{n} subprocesses"),
        ("flow.map.count.scheduled.one", "{n} tarea programada", "{n} scheduled task"),
        ("flow.map.count.scheduled.other", "{n} tareas programadas", "{n} scheduled tasks"),
        ("flow.map.count.rules.one", "{n} regla", "{n} rule"),
        ("flow.map.count.rules.other", "{n} reglas", "{n} rules"),

        ("flow.mapkey.title", "Qué es cada pieza", "What each piece is"),
        ("flow.mapkey.event", "Evento", "Event"),
        ("flow.mapkey.rule", "Regla", "Rule"),
        ("flow.mapkey.connector", "Conector a otro sistema", "Connector to another system"),
        ("flow.mapkey.person", "Tarea de una persona", "Task for a person"),
        ("flow.mapkey.ai", "Paso con IA", "AI step"),
        ("flow.mapkey.main.d", "Empieza con un evento o a mano y recorre fases.", "Starts from an event or by hand and runs through phases."),
        ("flow.mapkey.sub.d", "Un proceso que inicia otro, desde una de sus fases.", "A process that another process starts, from one of its phases."),
        ("flow.mapkey.scheduled.d", "Se ejecuta con un horario, sin un caso que lo inicie.", "Runs on a schedule, with no case to start it."),
        ("flow.mapkey.event.d", "Algo que ocurre e inicia un caso.", "Something that happens and starts a case."),
        ("flow.mapkey.rule.d", "Una tabla de decisión que consulta el proceso.", "A decision table the process asks."),
        ("flow.mapkey.connector.d", "Un enlace con otro sistema: lee de él o hace algo en él.", "A link to another system: the process reads from it or does something in it."),
        ("flow.mapkey.person.d", "Una tarea que espera a una persona en una cola.", "A task that waits for a person in a queue."),
        ("flow.mapkey.ai.d", "Un paso que consulta un modelo de inteligencia artificial.", "A step that asks an artificial intelligence model."),

        ("flow.map.lane.starts", "Lo que inicia un caso", "What starts a case"),
        ("flow.map.lane.detached", "Subprocesos a los que no llega nada", "Subprocesses nothing reaches"),
        ("flow.map.lane.shared", "Piezas que usa más de un proceso", "Pieces more than one process uses"),
        ("flow.map.starts", "inicia", "starts"),
        ("flow.map.uses", "usa", "uses"),
        ("flow.map.noStart", "Sin inicio definido: nada inicia este proceso por sí solo.", "No start defined: nothing starts this process on its own."),
        ("flow.map.phase.none", "Sin fase", "No phase"),
        ("flow.map.ends", "El caso termina en:", "The case ends in:"),
        ("flow.map.alsoStartedBy", "también lo inicia {title}", "also started by {title}"),
        ("flow.map.notLoaded", "no está cargado", "not loaded"),
        ("flow.map.cycles", "Estos procesos se inician unos a otros en bucle: {titles}.", "These processes start each other in a loop: {titles}."),
        ("flow.map.seeDiagram", "Ver el diagrama", "See the diagram"),
        ("flow.map.shared.used", "la usan {n} procesos", "used by {n} processes"),
        ("flow.map.routes.one", "{n} salida sin probar", "{n} route not tried"),
        ("flow.map.routes.other", "{n} salidas sin probar", "{n} routes not tried"),
        ("flow.map.tests.pass.one", "la prueba pasa", "the test passes"),
        ("flow.map.tests.pass.other", "pasan las {n} pruebas", "{n} tests pass"),
        ("flow.map.rule.open", "Abrir la regla {label}", "Open the rule {label}"),
        ("flow.map.authority", "Autoridad: {label}", "Authority: {label}"),

        ("flow.map.crumb.label", "Dónde está", "You are here"),
        ("flow.map.back", "Volver al mapa", "Back to the map"),
        ("flow.map.openFull", "Abrir el diagrama completo", "Open the full diagram"),
        ("flow.map.closeFull", "Volver a mostrar el mapa", "Show the map again"),
        ("flow.map.side.label", "Proceso abierto", "Open process"),

        ("flow.list.kind.none", "No se pudieron leer", "Could not be read"),
    ];
}

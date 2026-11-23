namespace Synapbiz.Brain.Authoring;

internal static partial class AuthoringText
{
    private static void AddTools()
    {
        AddSay();
        AddSimulation();
        AddRuleSay();
        AddProposalText();
        AddRefusals();
    }

    private static void AddSay()
    {
        T("say.guide.list", "Here are the sections of the guide. Read the working method first, then the sections you need.", "Aquí están las secciones de la guía. Lee primero el método de trabajo y después las secciones que necesites.");
        T("say.guide.section", "Here is that section of the guide.", "Esta es esa sección de la guía.");
        T("say.catalog.open", "The host has not limited what processes may use, so everything the format allows is available. There are {rules} approved rules.", "El anfitrión no ha limitado lo que pueden usar los procesos, así que está disponible todo lo que permite el formato. Hay {rules} reglas aprobadas.");
        T("say.catalog.some", "Available here: {ports} ports, {events} events, {queues} task queues and {rules} rules.", "Disponible aquí: {ports} puertos, {events} eventos, {queues} colas de tareas y {rules} reglas.");
        T("say.flow.list", "There are {n} approved processes and {d} drafts in progress.", "Hay {n} procesos aprobados y {d} borradores en curso.");
        T("say.flow.get", "The process {title} (version {version}) has {steps} steps; {pass} of its {total} tests pass.", "El proceso {title} (versión {version}) tiene {steps} pasos; {pass} de sus {total} pruebas pasan.");
        T("say.flow.notloading", "The approved process {id} is not accepted by Brain; its errors are listed.", "Brain no acepta el proceso aprobado {id}; sus errores están en la lista.");
        T("say.draft.refused", "Brain does not accept this draft yet: {n} problems. The first: {first}", "Brain todavía no acepta este borrador: {n} problemas. El primero: {first}");
        T("say.draft.tests", "{pass} of {total} tests pass.", "{pass} de {total} pruebas pasan.");
        T("say.draft.ready", "The draft of {title} is ready to propose. {tests}", "El borrador de {title} está listo para proponer. {tests}");
        T("say.draft.notready", "Brain accepts the draft of {title}, but it is not ready to propose: {n} things to fix. The first: {first} {tests}", "Brain acepta el borrador de {title}, pero no está listo para proponer: {n} cosas por corregir. La primera: {first} {tests}");
        T("say.scenario.added", "The test {name} was added and it passes.", "Se añadió la prueba {name} y pasa.");
        T("say.scenario.addedfailing", "The test {name} was added but it does not pass.", "Se añadió la prueba {name} pero no pasa.");
        T("change.nobase", "The approved process this draft came from cannot be read, so there is nothing to compare it with.", "No se puede leer el proceso aprobado del que viene este borrador, así que no hay con qué compararlo.");
    }

    private static void AddSimulation()
    {
        T("say.sim.entered", "The case went to {step}.", "El caso pasó a {step}.");
        T("say.sim.ignored", "That did not change the case: it was not waiting for it.", "Eso no cambió el caso: no lo estaba esperando.");
        T("say.sim.refused", "Brain did not accept that for the case right now.", "Brain no aceptó eso para el caso en este momento.");
        T("say.sim.waiting", "The case is at {step} and waits for {waits}.", "El caso está en {step} y espera {waits}.");
        T("say.sim.nothing", "nothing", "nada");
        T("say.sim.out.effect", "the answer of {port} for {step}", "la respuesta de {port} para {step}");
        T("say.sim.out.task", "a person in {queue} to choose {outcomes}", "que una persona en {queue} elija {outcomes}");
        T("say.sim.out.event", "the event {event}", "el evento {event}");
        T("say.sim.out.timer", "time to pass until {due}", "que pase el tiempo hasta {due}");
        T("say.sim.out.window", "the delivery window to open at {at}", "que se abra la ventana de envío a las {at}");
        T("say.sim.out.child", "the subprocess {process} to finish", "que termine el subproceso {process}");
        T("say.sim.completed", "The case ended with the result {result}.", "El caso terminó con el resultado {result}.");
        T("say.sim.cancelled", "The case was cancelled.", "El caso se canceló.");
        T("say.sim.suspended", "The case stopped at {step} on a problem and waits for an operator.", "El caso se detuvo en {step} por un problema y espera a un operador.");
        T("say.sim.closed", "The simulation was closed.", "Se cerró la simulación.");
    }

    private static void AddRuleSay()
    {
        T("say.rule.list", "There are {n} approved rules and {d} drafts in progress.", "Hay {n} reglas aprobadas y {d} borradores en curso.");
        T("say.rule.get", "The rule {title} (version {version}) has {rows} rows; {pass} of its {total} tests pass.", "La regla {title} (versión {version}) tiene {rows} filas; {pass} de sus {total} pruebas pasan.");
        T("say.rule.notloading", "The approved rule {id} is not accepted by Brain; its errors are listed.", "Brain no acepta la regla aprobada {id}; sus errores están en la lista.");
        T("say.rule.refused", "Brain does not accept this rule draft yet: {n} problems. The first: {first}", "Brain todavía no acepta este borrador de regla: {n} problemas. El primero: {first}");
        T("say.rule.ready", "The draft of the rule {title} is accepted and its tests pass ({pass} of {total}).", "El borrador de la regla {title} se acepta y sus pruebas pasan ({pass} de {total}).");
        T("say.rule.failing", "The draft of the rule {title} is accepted, but {n} of its tests do not pass. The first: {name}.", "El borrador de la regla {title} se acepta, pero {n} de sus pruebas no pasan. La primera: {name}.");
        T("say.rule.review.needed", "A proposal of this change would be reviewed by our team first.", "Una propuesta de este cambio la revisaría primero nuestro equipo.");
        T("say.rule.review.notneeded", "A proposal of this change stays within what the rule reads and gives, so the clinic that approves it can apply it.", "Una propuesta de este cambio se queda dentro de lo que la regla lee y da, así que la clínica que la apruebe puede aplicarla.");
        T("say.rule.sim.failed", "The engine refused these facts: {detail}", "El motor rechazó estos datos: {detail}");
        T("say.rule.sim.decided", "A row decides.", "Una fila decide.");
        T("say.rule.sim.defaultrow", "the default row", "la fila por defecto");
        T("say.rule.sim.row", "Row {row} decides: {sentence}", "Decide la fila {row}: {sentence}");
        T("say.rule.sim.none", "No row applies to these facts, so the rule decides nothing.", "Ninguna fila se aplica a estos datos, así que la regla no decide nada.");
        T("say.rule.test.pass", "All tests pass. {uncovered} rows are not reached by any passing test.", "Todas las pruebas pasan. {uncovered} filas no las alcanza ninguna prueba que pase.");
        T("say.rule.test.fail", "{n} tests do not pass. The first: {name}.", "{n} pruebas no pasan. La primera: {name}.");
        T("rule.review.kind_changed", "the kind of decision changed", "cambió el tipo de decisión");
        T("rule.review.hit_policy_changed", "the way rows are chosen changed", "cambió la forma de elegir las filas");
        T("rule.review.new_fact_read", "a row now reads a fact that no row read before", "una fila lee ahora un dato que ninguna fila leía antes");
        T("rule.review.new_outcome_field", "a row now gives a result field that no row gave before", "una fila da ahora un campo de resultado que ninguna fila daba antes");
        T("rule.review.new_outcome_value", "a row now gives a code or category that no row gave before", "una fila da ahora un código o categoría que ninguna fila daba antes");
    }

    private static void AddProposalText()
    {
        T("say.propose.flow", "The proposal for the process {id} is written at {location}. It changes nothing: our team reviews it before anything is activated.", "La propuesta para el proceso {id} está escrita en {location}. No cambia nada: nuestro equipo la revisa antes de activar algo.");
        T("say.propose.rule.review", "The proposal for the rule {id} is written at {location}. It goes beyond what the rule reads or gives, so our team reviews it first. It changes nothing until then.", "La propuesta para la regla {id} está escrita en {location}. Va más allá de lo que la regla lee o da, así que la revisa primero nuestro equipo. No cambia nada hasta entonces.");
        T("say.propose.rule.clinic", "The proposal for the rule {id} is written at {location}. It stays within what the rule reads and gives. It changes nothing until the person who approves it applies it.", "La propuesta para la regla {id} está escrita en {location}. Se queda dentro de lo que la regla lee y da. No cambia nada hasta que la persona que la aprueba la aplique.");
        T("say.proposal.list", "There are {n} proposals.", "Hay {n} propuestas.");
        T("say.proposal.get", "Here is the proposal {name}.", "Esta es la propuesta {name}.");
        T("proposal.flow.head", "Proposal to change the process {id} (new version {version}).", "Propuesta para cambiar el proceso {id} (nueva versión {version}).");
        T("proposal.flow.head.new", "Proposal for a new process {id} (version {version}).", "Propuesta de un proceso nuevo {id} (versión {version}).");
        T("proposal.rule.head", "Proposal to change the rule {id} (new version {version}).", "Propuesta para cambiar la regla {id} (nueva versión {version}).");
        T("proposal.by", "Asked by {author} on {date}.", "Lo pidió {author} el {date}.");
        T("proposal.why", "Why: {summary}", "Motivo: {summary}");
        T("proposal.changes", "What changes:", "Qué cambia:");
        T("proposal.behaviour", "What happens to the tests of the earlier version:", "Qué pasa con las pruebas de la versión anterior:");
        T("proposal.tests", "Tests: {pass} of {total} pass. {covered} of {routes} routes are taken by a test.", "Pruebas: {pass} de {total} pasan. Una prueba recorre {covered} de {routes} rutas.");
        T("proposal.gap", "Accepted knowingly at {step}: {reason}", "Aceptado a sabiendas en {step}: {reason}");
        T("proposal.review.flow", "Our team reviews this proposal before it is activated.", "Nuestro equipo revisa esta propuesta antes de activarla.");
        T("proposal.review.rule.yes", "Our team reviews this proposal first, because {reasons}.", "Nuestro equipo revisa primero esta propuesta, porque {reasons}.");
        T("proposal.review.rule.no", "It stays within the facts the rule reads and the results it gives, so the clinic that approves it can apply it.", "Se queda dentro de los datos que la regla lee y los resultados que da, así que la clínica que la apruebe puede aplicarla.");
        T("proposal.nothing", "Nothing changes until a person applies the proposal.", "Nada cambia hasta que una persona aplique la propuesta.");
    }

    private static void AddRefusals()
    {
        T("refuse.input.invalid", "The request to {tool} was not valid: {detail}", "La petición a {tool} no era válida: {detail}");
        T("refuse.workspace.store_failed", "The workspace refused that: {detail}", "El espacio de trabajo lo rechazó: {detail}");
        T("refuse.workspace.id_invalid", "That name is not valid: {detail}", "Ese nombre no es válido: {detail}");
        T("refuse.tool.unknown", "There is no tool called {tool}.", "No existe ninguna herramienta llamada {tool}.");
        T("refuse.catalog.invalid", "The catalog of this workspace is not valid ({detail}); the host must fix it.", "El catálogo de este espacio de trabajo no es válido ({detail}); el anfitrión debe corregirlo.");
        T("refuse.draft.not_found", "There is no draft called {id}.", "No hay ningún borrador llamado {id}.");
        T("refuse.draft.not_json", "The draft {id} is not a readable document, so it cannot be changed in parts.", "El borrador {id} no es un documento legible, así que no se puede cambiar por partes.");
        T("refuse.draft.too_large", "The document is longer than the {max} characters this workspace accepts.", "El documento supera los {max} caracteres que acepta este espacio de trabajo.");
        T("refuse.draft.id_mismatch", "The draft is called {id} but the document says its id is {found}; they must be the same.", "El borrador se llama {id} pero el documento dice que su id es {found}; deben ser iguales.");
        T("refuse.draft.base_not_found", "There is no approved process called {id} to start from.", "No hay ningún proceso aprobado llamado {id} del que partir.");
        T("refuse.flow.not_found", "There is no approved process called {id}.", "No hay ningún proceso aprobado llamado {id}.");
        T("refuse.patch.failed", "Change number {n} could not be applied, so nothing was changed: {detail}", "No se pudo aplicar el cambio número {n}, así que no se cambió nada: {detail}");
        T("refuse.scenario.source", "Give either a simulation or a written test, not both and not neither.", "Da una simulación o una prueba escrita, ni las dos ni ninguna.");
        T("refuse.scenario.draft_not_loaded", "Brain does not accept the draft {id} yet, so a test cannot be added. Fix its errors first.", "Brain todavía no acepta el borrador {id}, así que no se puede añadir una prueba. Corrige antes sus errores.");
        T("refuse.scenario.name_taken", "A test called {name} already exists.", "Ya existe una prueba llamada {name}.");
        T("refuse.scenario.invalid", "That test does not fit the process: {detail}", "Esa prueba no encaja con el proceso: {detail}");
        T("refuse.scenario.not_writable", "That simulation cannot be turned into a test: {detail}", "Esa simulación no se puede convertir en una prueba: {detail}");
        T("refuse.simulation.not_found", "That simulation is not running any more; it may have expired. Start a new one.", "Esa simulación ya no está en marcha; puede que haya caducado. Empieza otra.");
        T("refuse.simulation.draft_changed", "The draft changed after that simulation started. Run the case again on the current draft.", "El borrador cambió después de empezar esa simulación. Repite el caso con el borrador actual.");
        T("refuse.simulation.draft_not_loaded", "Brain does not accept the draft {id} yet, so no case can run on it.", "Brain todavía no acepta el borrador {id}, así que no se puede ejecutar ningún caso con él.");
        T("refuse.simulation.limit", "Too many simulations are running ({max}). Close one or wait.", "Hay demasiadas simulaciones en marcha ({max}). Cierra una o espera.");
        T("refuse.simulation.start_invalid", "The case could not be started: {detail}", "No se pudo empezar el caso: {detail}");
        T("refuse.simulation.target", "Say which simulation to use, or which draft to start one on.", "Indica qué simulación usar, o con qué borrador empezar una.");
        T("refuse.simulation.command_failed", "That could not be done to the case: {detail}", "No se pudo hacer eso con el caso: {detail}");
        T("refuse.rule.not_found", "There is no approved rule called {id}.", "No hay ninguna regla aprobada llamada {id}.");
        T("refuse.rule.not_loading", "Brain does not accept the rule {id}, so this cannot be done.", "Brain no acepta la regla {id}, así que no se puede hacer esto.");
        T("refuse.rule.edit_invalid", "Change number {n} cannot be used: {detail}", "El cambio número {n} no se puede usar: {detail}");
        T("refuse.rule.edit_failed", "A change to the rule could not be applied: {detail}", "No se pudo aplicar un cambio a la regla: {detail}");
        T("refuse.rule.facts_invalid", "These facts cannot be used: {detail}", "Estos datos no se pueden usar: {detail}");
        T("refuse.proposal.not_ready", "This draft is not ready to propose: {n} things to fix. The first: {first}", "Este borrador no está listo para proponer: {n} cosas por corregir. La primera: {first}");
        T("refuse.proposal.inconsistent", "The proposal does not hold together (reason {code}); see the details.", "La propuesta no es coherente (motivo {code}); mira los detalles.");
        T("refuse.proposal.base_stale", "The approved version of {id} changed after the draft was made. Make the draft again from the current version.", "La versión aprobada de {id} cambió después de hacer el borrador. Haz el borrador de nuevo desde la versión actual.");
        T("refuse.proposal.not_found", "There is no proposal called {name}.", "No hay ninguna propuesta llamada {name}.");
        T("refuse.proposal.unreadable", "The file {name} is not a proposal this version can read.", "El archivo {name} no es una propuesta que esta versión pueda leer.");
    }
}

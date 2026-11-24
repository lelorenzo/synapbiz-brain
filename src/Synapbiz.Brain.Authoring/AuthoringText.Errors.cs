namespace Synapbiz.Brain.Authoring;

/// <summary>The sentence for every code of the loaders, validators and the interpreter that can reach a person. The engine's own text never does.</summary>
internal static partial class AuthoringText
{
    /// <summary>The prefix of the keys of the sentences of the error codes.</summary>
    internal const string ErrorPrefix = "err.";

    private static void AddErrors()
    {
        // The sentences end with "{where}": a phrase such as "in the step “Wait for the signed note”", written from the labels of the document.
        void E(string code, string english, string spanish) => T(ErrorPrefix + code, english + ", {where}.", spanish + ", {where}.");

        T("err.generic", "Something here cannot be used as it is, {where}. Ask the assistant to explain what to change.", "Hay algo aquí que no se puede usar tal como está, {where}. Pide al asistente que explique qué cambiar.");

        // The document as a whole.
        E("spec.json.invalid", "The text cannot be read as a document: it is malformed, has a number that is not finite, or repeats a name", "El texto no se puede leer como documento: está mal formado, tiene un número que no es finito o repite un nombre");
        E("spec.structure.envelope", "The header of the document is incomplete or wrong", "El encabezado del documento está incompleto o es incorrecto");
        E("spec.structure.unknown_format", "The document does not say which kind of document it is", "El documento no dice de qué tipo es");
        E("spec.structure.schema", "A value does not have the shape the format asks for", "Un valor no tiene la forma que pide el formato");
        E("spec.semantics.node", "A name or a version is not valid", "Un nombre o una versión no es válido");
        E("spec.semantics.duplicate_id", "Two items have the same name", "Dos elementos tienen el mismo nombre");
        E("spec.semantics.reference", "Something points to an item that does not exist", "Algo apunta a un elemento que no existe");
        E("spec.semantics.cycle", "Some items point to each other in a circle", "Algunos elementos se apuntan entre sí en círculo");
        E("spec.semantics.translation", "A text in one of the two languages is not valid", "Un texto en uno de los dos idiomas no es válido");

        // Rules.
        E("brain.contract.malformed", "A contract the rule depends on cannot be read", "No se puede leer un contrato del que depende la regla");
        E("brain.contract.module_required", "The rule does not list the contract of the module it belongs to", "La regla no lista el contrato del módulo al que pertenece");
        E("brain.contract.unavailable", "The rule needs a contract that this workspace does not have; the contracts of the catalog are needed for a rule or a process to load", "La regla necesita un contrato que este espacio de trabajo no tiene; los contratos del catálogo son necesarios para que una regla o un proceso se carguen");
        E("decisions.kind.not_registered", "The rule uses a kind of decision that no available contract declares", "La regla usa un tipo de decisión que ningún contrato disponible declara");
        E("decisions.kind.key_invalid", "The kind of decision is not written the way the contract writes it", "El tipo de decisión no está escrito como lo escribe el contrato");
        E("decisions.definition.malformed", "The rows of the rule cannot be read as a table of decisions", "Las filas de la regla no se pueden leer como una tabla de decisiones");
        E("decisions.validation.duplicate_row_key", "Two rows have the same key", "Dos filas tienen la misma clave");
        E("decisions.validation.unknown_fact", "A row reads a fact that the kind of decision does not have", "Una fila lee un dato que el tipo de decisión no tiene");
        E("decisions.condition.operator_not_allowed", "A condition uses a comparison that does not suit the kind of that fact", "Una condición usa una comparación que no corresponde al tipo de ese dato");
        E("decisions.validation.money_currency_mismatch", "Amounts of money compared with the same fact use different currencies", "Importes de dinero comparados con el mismo dato usan monedas distintas");
        E("decisions.condition.range_type_mismatch", "A range does not have ends of the kind of its fact", "Un rango no tiene extremos del tipo de su dato");
        E("decisions.validation.cell_type_mismatch", "A value in a condition is not of the kind of its fact", "Un valor de una condición no es del tipo de su dato");
        E("decisions.validation.type_mismatch", "A value is not of the kind its fact or result field has", "Un valor no es del tipo de su dato o campo de resultado");
        E("brain.fact.label_source_mismatch", "A fact that has a source of labels is not a reference", "Un dato que tiene una fuente de etiquetas no es una referencia");
        E("decisions.facts.missing", "A test case lacks a fact that the rule needs", "A un caso de prueba le falta un dato que la regla necesita");
        E("decisions.facts.type_mismatch", "A test case gives a fact a value of the wrong kind", "Un caso de prueba da a un dato un valor de otro tipo");
        E("decisions.facts.too_large", "The facts given are larger than the rule accepts", "Los datos dados son más grandes de lo que acepta la regla");
        E("brain.golden.unknown_row", "A test case expects a row that does not exist", "Un caso de prueba espera una fila que no existe");
        E("brain.outcome.unknown_field", "A result gives a field that the kind of decision does not have", "Un resultado da un campo que el tipo de decisión no tiene");
        E("brain.outcome.type_mismatch", "A result field has a value of the wrong kind", "Un campo de resultado tiene un valor de otro tipo");
        E("brain.outcome.required_field", "A result lacks a field that the kind of decision requires", "A un resultado le falta un campo que el tipo de decisión exige");
        E("brain.rule.golden_outcome_missing", "A test case does not say which result it expects", "Un caso de prueba no dice qué resultado espera");
        E("brain.fact.enum_not_allowed", "A fact has a value it does not allow", "Un dato tiene un valor que no admite");
        E("decisions.validation.hit_policy_unsupported", "The way the rule chooses rows is not one the engine supports", "La forma en que la regla elige filas no es una que el motor admita");
        E("decisions.validation.incomplete", "Some combinations of facts have no decision", "Algunas combinaciones de datos no tienen decisión");
        E("decisions.validation.overlap", "Two rows can apply to the same facts", "Dos filas pueden aplicarse a los mismos datos");
        E("decisions.validation.unreachable", "A row can never be reached because an earlier row always applies first", "Una fila nunca se alcanza porque una fila anterior siempre se aplica primero");
        E("decisions.validation.outcome_invalid", "The result of a row does not have the fields and kinds the rule requires", "El resultado de una fila no tiene los campos y tipos que la regla exige");
        E("decisions.validation.golden_failed", "The rule decides something other than a test case expects", "La regla decide algo distinto de lo que espera un caso de prueba");
        E("decisions.validation.golden_row_uncovered", "No test case reaches a row", "Ningún caso de prueba llega a una fila");
        E("decisions.validation.golden_set_empty", "The rule has no test cases", "La regla no tiene casos de prueba");
        E("decisions.evaluation.multiple_matches", "More than one row applied where only one may", "Se aplicó más de una fila donde solo puede aplicarse una");
        E("decisions.composition.resolver_required", "The rule uses a fact built from others that this workspace cannot resolve", "La regla usa un dato construido a partir de otros que este espacio de trabajo no puede resolver");
        E("decisions.composition.cycle", "Facts built from other facts depend on each other in a circle", "Datos construidos a partir de otros dependen unos de otros en círculo");
        E("decisions.composition.too_deep", "Facts are built from other facts through too many levels", "Hay demasiados niveles de datos construidos a partir de otros");
        E("decisions.table.unknown_fact", "The table reads a fact that the kind of decision does not have", "La tabla lee un dato que el tipo de decisión no tiene");
        E("decisions.allowSelfApproval", "This is a setting of the host and not something a document may say", "Esto es un ajuste del anfitrión y no algo que un documento pueda decir");

        // Processes: references and structure.
        E("process.step.unknown", "A route or the start names a step that does not exist", "Una ruta o el inicio nombra un paso que no existe");
        E("process.step.unreachable", "No route leads to a step, so it can never happen", "Ninguna ruta lleva a un paso, así que nunca puede ocurrir");
        E("process.stage.unknown", "A step belongs to a stage that is not listed", "Un paso pertenece a una etapa que no está en la lista");
        E("process.variable.unknown", "A piece of information is used without having been declared", "Se usa un dato sin haberlo declarado");
        E("process.variable.type", "A piece of information is of a kind that this use cannot take", "Un dato es de un tipo que este uso no admite");
        E("process.variable.initial", "The starting value of a piece of information does not fit its kind", "El valor inicial de un dato no corresponde a su tipo");
        E("process.variable.type_mismatch", "A value does not have the kind of the information it fills", "Un valor no tiene el tipo del dato que rellena");
        E("process.rule.unavailable", "The process names a rule that is not approved here or does not load", "El proceso nombra una regla que no está aprobada aquí o no se carga");
        E("process.rule.fact_unknown", "A step gives the rule a fact that the rule does not read", "Un paso da a la regla un dato que la regla no lee");
        E("process.rule.outcome_unknown", "A step uses a result field that the rule does not give", "Un paso usa un campo de resultado que la regla no da");
        E("process.rule.hit_policy", "The way the step uses the result does not suit how the rule chooses rows", "La forma en que el paso usa el resultado no corresponde a cómo la regla elige filas");
        E("process.route.incomplete", "The routes do not cover every value the result can take", "Las rutas no cubren todos los valores que puede tomar el resultado");
        E("process.route.unknown_value", "A route is for a value that the result never takes", "Hay una ruta para un valor que el resultado nunca toma");
        E("process.uses.missing", "The list of rules and processes the document uses lacks one that a step needs", "A la lista de reglas y procesos que usa el documento le falta uno que un paso necesita");
        E("process.uses.unused", "The list of rules and processes the document uses holds one that no step needs", "La lista de reglas y procesos que usa el documento tiene uno que ningún paso necesita");
        E("process.subprocess.unavailable", "A step starts another process that does not exist in this workspace", "Un paso inicia otro proceso que no existe en este espacio de trabajo");
        E("process.ai.next_not_decide", "An AI step goes on to something other than a decision; only a rule may move the case", "Un paso de IA sigue hacia algo que no es una decisión; solo una regla puede mover el caso");
        E("process.ai.schema_invalid", "The description of what an AI step receives or may return cannot be used", "No se puede usar la descripción de lo que recibe o puede devolver un paso de IA");
        E("process.cycle.unbounded", "The steps go round in a circle that never has to end", "Los pasos giran en un círculo que nunca tiene que terminar");
        E("process.duration.invalid", "A length of time is not valid; it must be between one second and 3660 days", "Una duración no es válida; debe estar entre un segundo y 3660 días");
        E("process.scenario.invalid", "A test does not fit the process", "Una prueba no encaja con el proceso");
        E("process.action.window_required", "A step that contacts a person has no delivery window", "Un paso que contacta a una persona no tiene ventana de envío");
        E("process.action.interrupt_requires_window", "A step ends its wait for an event but has no delivery window to wait for", "Un paso termina su espera por un aviso pero no tiene ventana de envío que esperar");
        E("process.window.invalid", "A delivery window is not well described: its zone, days or hours", "Una ventana de envío no está bien descrita: su zona, sus días o sus horas");
        E("process.window.unknown", "A step uses a delivery window that is not declared", "Un paso usa una ventana de envío que no está declarada");
        E("process.window.zone_unknown", "The test or simulation gave no usable time zone for a delivery window", "La prueba o la simulación no dio una zona horaria utilizable para una ventana de envío");
        E("process.trigger.event_duplicate", "Two ways of starting the process wait for the same event", "Dos formas de iniciar el proceso esperan el mismo aviso");
        E("process.trigger.schedule_duplicate", "Two scheduled starts have the same name", "Dos inicios programados tienen el mismo nombre");
        E("process.trigger.cron_invalid", "A schedule is not written correctly", "Un horario no está escrito correctamente");
        E("process.trigger.zone_invalid", "The time zone of a schedule is not valid", "La zona horaria de un horario no es válida");
        E("process.trigger.input_source_unknown", "A start fills a piece of information from something it cannot give", "Un inicio rellena un dato con algo que no puede dar");
        E("process.trigger.variable_unknown", "A start fills a piece of information that is not declared", "Un inicio rellena un dato que no está declarado");
        E("process.trigger.variable_not_input", "A start fills a piece of information that is not given when the case starts", "Un inicio rellena un dato que no se da al empezar el caso");
        E("process.poll.too_many_rounds", "A repeated read would make too many rounds", "Una lectura repetida haría demasiadas vueltas");
        E("process.display.invalid", "The names shown to people are not written correctly", "Los nombres que ven las personas no están escritos correctamente");
        E("process.display.unknown_id", "A name is given to something the process does not have", "Se da un nombre a algo que el proceso no tiene");

        // Running a case.
        E("process.state.malformed", "The saved case cannot be read", "No se puede leer el caso guardado");
        E("process.input.unknown", "A value was given that the process does not take when a case starts", "Se dio un valor que el proceso no recibe al empezar un caso");
        E("process.definition.mismatch", "The case belongs to another version of the process", "El caso pertenece a otra versión del proceso");
        E("process.trigger.stale", "The case is not waiting for that", "El caso no estaba esperando eso");
        E("process.event.not_accepted", "The case is not waiting for that event", "El caso no espera ese aviso");
        E("process.task.outcome_unknown", "That is not one of the choices the task offers", "Esa no es una de las opciones que ofrece la tarea");
        E("process.effect.output_malformed", "The answer does not have the shape the step expects", "La respuesta no tiene la forma que el paso espera");
        E("process.ai.input_invalid", "What the AI step sends does not fit its description", "Lo que envía el paso de IA no encaja con su descripción");
        E("process.ai.output_invalid", "The answer of the AI does not fit what the step allows it to return", "La respuesta de la IA no encaja con lo que el paso le permite devolver");
        E("process.route.unmatched", "The result that came out has no route", "El resultado que salió no tiene ruta");
        E("process.timer.until_missing", "A timer needs a date and time that was never given", "Un temporizador necesita una fecha y hora que nunca se dio");
        E("process.timeout.instant_missing", "A time limit needs a date and time that was never given", "Un límite de tiempo necesita una fecha y hora que nunca se dio");
        E("process.wait.correlation_missing", "A wait needs a value to match the event with that was never given", "Una espera necesita un valor para reconocer el aviso que nunca se dio");
        E("process.wait.correlation_invalid", "The value to match the event with is not usable", "El valor para reconocer el aviso no se puede usar");
        E("process.loop.not_a_list", "A repetition goes through something that is not a list", "Una repetición recorre algo que no es una lista");
        E("process.limits.max_steps", "The case used up the number of steps it is allowed", "El caso agotó el número de pasos que se le permite");
        E("process.window.zone_missing", "A delivery window needs a time zone that was never given", "Una ventana de envío necesita una zona horaria que nunca se dio");
        E("process.child.cancelled", "A process started by this one was cancelled", "Un proceso iniciado por este se canceló");
        E("process.definition.unavailable", "The version of the process the case belongs to is not available", "La versión del proceso a la que pertenece el caso no está disponible");
        E("process.effect.outcome_unknown", "The step has no result like that", "El paso no tiene un resultado así");
        E("process.instance.not_found", "There is no such case", "No existe ese caso");
        E("process.parent.cancelled", "The process that started this one was cancelled", "El proceso que inició este se canceló");
        E("process.port.exception", "The connection failed unexpectedly", "La conexión falló de forma inesperada");
        E("process.port.not_registered", "The connection is not set up", "La conexión no está configurada");
        E("process.port.timeout", "The connection did not answer in time", "La conexión no respondió a tiempo");
        E("process.purge.busy", "The case is busy and cannot be removed now", "El caso está ocupado y no se puede quitar ahora");
        E("process.purge.not_finished", "The case has not finished, so it cannot be removed", "El caso no ha terminado, así que no se puede quitar");
        E("process.purge.not_root", "Only a case that no other case started can be removed", "Solo se puede quitar un caso que ningún otro caso inició");

        // The workbench codes behind the tools.
        E("workbench.draft.baseNotReadable", "The approved rule cannot be edited as a table of decisions", "La regla aprobada no se puede editar como tabla de decisiones");
        E("workbench.draft.rowNotFound", "There is no such row", "No existe esa fila");
        E("workbench.draft.rowKeyTaken", "Another row already has that key", "Otra fila ya tiene esa clave");
        E("workbench.draft.rowKeyNotValid", "That key is not valid for a row", "Esa clave no es válida para una fila");
        E("workbench.draft.conditionNotFound", "The row has no condition on that fact", "La fila no tiene ninguna condición sobre ese dato");
        E("workbench.draft.noDefaultRow", "The rule has no default row", "La regla no tiene fila por defecto");
        E("workbench.draft.caseNotFound", "There is no such test case", "No existe ese caso de prueba");
        E("workbench.draft.caseNameTaken", "Another test case already has that name", "Otro caso de prueba ya tiene ese nombre");
        E("workbench.draft.caseNotValid", "The test case does not fit how the rule chooses rows", "El caso de prueba no encaja con cómo la regla elige filas");
        E("workbench.draft.moveOntoItself", "A row cannot be moved after itself", "Una fila no se puede mover después de sí misma");
        E("workbench.draft.labelNotValid", "The name given to the row has no visible character", "El nombre dado a la fila no tiene ningún carácter visible");
        E("workbench.fact.required", "A value is needed", "Falta un valor");
        E("workbench.fact.value_not_allowed", "That value is not one of the values allowed", "Ese valor no es uno de los valores permitidos");
        E("workbench.fact.not_a_boolean", "A yes or no is needed", "Se necesita un sí o un no");
        E("workbench.fact.not_an_integer", "A whole number is needed", "Se necesita un número entero");
        E("workbench.fact.not_a_decimal", "A number with decimals is needed", "Se necesita un número con decimales");
        E("workbench.fact.not_a_date", "A date written year-month-day is needed", "Se necesita una fecha escrita año-mes-día");
        E("workbench.fact.not_money", "An amount of money with its currency is needed, for example 10.50 USD", "Se necesita un importe con su moneda, por ejemplo 10.50 USD");
        E("workbench.fact.not_a_reference", "A reference in the form the rule expects is needed", "Se necesita una referencia con la forma que la regla espera");
        E("workbench.flow.json_invalid", "The values given cannot be read", "No se pueden leer los valores dados");
        E("workbench.flow.duration_invalid", "That length of time is not valid; it must be between one second and 3660 days", "Esa duración no es válida; debe estar entre un segundo y 3660 días");
        E("workbench.flow.not_started", "The case did not start", "El caso no empezó");
        E("workbench.flow.answer_source_failed", "The source of answers failed", "La fuente de respuestas falló");
        E("workbench.flow.effect_unknown", "The case is not waiting for that answer", "El caso no espera esa respuesta");
        E("workbench.flow.route_unexplained", "The history of a test does not say which route it took", "El historial de una prueba no dice qué ruta tomó");
        E("workbench.flow.zone_invalid", "A time zone or its offset is not valid", "Una zona horaria o su desfase no es válido");
        E("workbench.flow.child_unknown", "A process started by this one is not among the processes of this workspace", "Un proceso iniciado por este no está entre los procesos de este espacio de trabajo");
        E("workbench.flow.cancelled", "The case was cancelled", "El caso se canceló");
        E("workbench.flow_review.json_invalid", "The review cannot be read", "No se puede leer la revisión");
        E("workbench.flow_review.unknown_format", "The review is not in a format this version knows", "La revisión no está en un formato que esta versión conozca");
        E("workbench.flow_review.malformed", "The review is not written correctly", "La revisión no está escrita correctamente");
        E("workbench.flow_review.stale", "The review is for another version of the process", "La revisión es de otra versión del proceso");
        E("workbench.flow_review.target_unknown", "The review marks something the process does not have", "La revisión marca algo que el proceso no tiene");
        E("workbench.flow_review.scenario_invalid", "A test proposed in the review does not fit the process", "Una prueba propuesta en la revisión no encaja con el proceso");
        E("workbench.flow_review.scenario_not_object", "A test proposed in the review is not written correctly", "Una prueba propuesta en la revisión no está escrita correctamente");
        E("workbench.review.json_invalid", "The review cannot be read", "No se puede leer la revisión");
        E("workbench.review.unknown_format", "The review is not in a format this version knows", "La revisión no está en un formato que esta versión conozca");
        E("workbench.review.malformed", "The review is not written correctly", "La revisión no está escrita correctamente");
        E("workbench.proposal.notJson", "The proposal cannot be read", "No se puede leer la propuesta");
        E("workbench.proposal.specNotSupported", "The proposal is not in a format this version knows", "La propuesta no está en un formato que esta versión conozca");
        E("workbench.proposal.fieldMissing", "The proposal lacks something it needs", "A la propuesta le falta algo que necesita");
        E("workbench.proposal.fieldNotValid", "Something in the proposal is of the wrong kind", "Algo de la propuesta es de un tipo equivocado");
        T(ErrorPrefix + "authoring.proposal.needs_review_false", "The proposal says it needs no review, but every process proposal does.", "La propuesta dice que no necesita revisión, pero toda propuesta de proceso la necesita.");
        T(ErrorPrefix + "authoring.proposal.hash_mismatch", "The document of the proposal is not the one the proposal describes.", "El documento de la propuesta no es el que la propuesta describe.");
        T(ErrorPrefix + "authoring.proposal.document_invalid", "The document of the proposal is not accepted by Brain.", "Brain no acepta el documento de la propuesta.");
        T(ErrorPrefix + "authoring.proposal.identity_mismatch", "The process of the proposal is not the one it names, or it is not the one it was made from.", "El proceso de la propuesta no es el que nombra, o no es aquel a partir del cual se hizo.");
        T(ErrorPrefix + "authoring.proposal.tests_failing", "A test of the process does not pass.", "Una prueba del proceso no pasa.");
        T(ErrorPrefix + "authoring.proposal.base_stale", "The approved process changed after the proposal was made.", "El proceso aprobado cambió después de hacer la propuesta.");
        T(ErrorPrefix + "authoring.proposal.version_not_newer", "The version number must be greater than the current one; raise it and save again.", "El número de versión debe ser mayor que el actual; súbelo y guarda de nuevo.");
    }
}

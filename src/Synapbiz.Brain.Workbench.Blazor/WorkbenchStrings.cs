using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Workbench.Blazor;

/// <summary>
/// The fixed strings of the panels, in Spanish and English. Every key can be replaced by the host through
/// <c>RuleWorkbench.TextOverrides</c>, except that the four markings (<c>band.simulate</c>, <c>band.tests</c>,
/// <c>pill.simulation</c> and <c>notice.privacy</c>) may be reworded but never emptied: an override that is null, empty or
/// white space is ignored and the default is used. In a string, a <c>**bold**</c> pair renders as <c>strong</c>; nothing else
/// is interpreted and every text is HTML-encoded. A <c>{name}</c> placeholder is filled by the panel.
/// </summary>
public static class WorkbenchStrings
{
    private static readonly string[] MandatoryKeys = ["band.simulate", "band.tests", "pill.simulation", "notice.privacy"];

    private static readonly (string Key, string Es, string En)[] Table =
    [
        ("tabs.label", "Secciones de la regla", "Rule sections"),
        ("tabs.read", "Leer", "Read"),
        ("tabs.simulate", "Simular", "Simulate"),
        ("tabs.tests", "Casos de prueba", "Test cases"),
        ("tabs.review", "Revisión", "Review"),
        ("fail.count.one", "{n} falla", "{n} fails"),
        ("fail.count.other", "{n} fallan", "{n} fail"),

        ("list.title", "Reglas", "Rules"),
        ("list.aria", "Lista de reglas", "Rule list"),
        ("list.count.rules.one", "{n} regla", "{n} rule"),
        ("list.count.rules.other", "{n} reglas", "{n} rules"),
        ("list.count.approved.one", "{n} aprobada", "{n} approved"),
        ("list.count.approved.other", "{n} aprobadas", "{n} approved"),
        ("list.count.drafts.one", "{n} en borrador", "{n} draft"),
        ("list.count.drafts.other", "{n} en borrador", "{n} drafts"),
        ("list.rows.one", "{n} fila", "{n} row"),
        ("list.rows.other", "{n} filas", "{n} rows"),
        ("list.casesPass.one", "{p} de {t} caso pasa", "{p} of {t} case passes"),
        ("list.casesPass.other", "{p} de {t} casos pasan", "{p} of {t} cases pass"),
        ("list.casesFail.single", "el caso falla", "the case fails"),
        ("list.casesFail.one", "{f} de {t} casos falla", "{f} of {t} cases fails"),
        ("list.casesFail.other", "{f} de {t} casos fallan", "{f} of {t} cases fail"),
        ("list.noCases", "sin casos de prueba", "no test cases"),
        ("list.review.none", "sin revisar", "not reviewed"),
        ("list.review.partial", "revisión en curso", "review in progress"),
        ("list.review.done", "revisada", "reviewed"),
        ("list.review.questions.one", "{n} pregunta", "{n} question"),
        ("list.review.questions.other", "{n} preguntas", "{n} questions"),
        ("list.review.fixes.one", "{n} por corregir", "{n} to fix"),
        ("list.review.fixes.other", "{n} por corregir", "{n} to fix"),
        ("list.unreadable", "No se pudo leer", "Could not be read"),
        ("list.empty", "No hay reglas cargadas en este equipo.", "No rules are loaded on this computer."),

        ("load.loading", "Cargando reglas…", "Loading rules…"),
        ("load.failedBody", "Esta regla no se pudo leer en este equipo.", "This rule could not be read on this computer."),
        ("load.untitled", "Regla sin título", "Untitled rule"),

        ("state.draft", "Borrador", "Draft"),
        ("state.approved", "Aprobada", "Approved"),
        ("state.draftLine", "Borrador: falta la aprobación.", "Draft: approval is pending."),
        ("state.approvedLine", "Aprobada.", "Approved."),
        ("state.elsewhere", "Las reglas no se aprueban ni se activan desde esta pantalla.", "Rules are not approved or activated from this screen."),
        ("panel.version", "Versión {n}", "Version {n}"),

        ("read.lead.first", "Esta regla decide en este orden. Gana la primera fila que se cumple.", "This rule decides in this order. The first row that matches wins."),
        ("read.lead.unique", "Solo una fila puede cumplirse a la vez.", "Only one row can match at a time."),
        ("read.lead.collect", "Se aplican todas las filas que se cumplen.", "Every row that matches applies."),
        ("read.rowTextPrefix", "Texto que se muestra:", "Text shown:"),
        ("read.noDefaultRow", "En cualquier otro caso, la regla no decide.", "Otherwise, the rule does not decide."),
        ("read.facts", "Datos que usa", "Data it uses"),
        ("read.required", "obligatorio", "required"),
        ("read.optional", "opcional", "optional"),
        ("read.type.code", "Texto", "Text"),
        ("read.type.enum", "Una opción", "One option"),
        ("read.type.boolean", "Sí o no", "Yes or no"),
        ("read.type.integer", "Número entero", "Whole number"),
        ("read.type.decimal", "Número", "Number"),
        ("read.type.date", "Fecha", "Date"),
        ("read.type.money", "Monto", "Amount"),
        ("read.type.reference", "Referencia", "Reference"),
        ("row.label", "Fila {n}", "Row {n}"),
        ("row.always", "Se cumple siempre.", "Always met."),

        ("review.confirm", "Confirmar", "Confirm"),
        ("review.fix", "Corregir", "Fix"),
        ("review.question", "Pregunta", "Question"),
        ("review.confirmed", "Confirmada", "Confirmed"),
        ("review.fixState", "Corregir", "Fix"),
        ("review.questionState", "Pregunta", "Question"),
        ("review.groupRow", "Revisión de la fila {n}", "Review of row {n}"),
        ("review.groupRule", "Revisión de la regla completa", "Review of the whole rule"),
        ("review.groupCase", "Revisión del caso {name}", "Review of the case {name}"),
        ("review.noteLabel", "Nota para quien mantiene la regla", "Note for the rule's maintainer"),
        ("review.noteShort", "Nota", "Note"),
        ("review.addNote", "Agregar una nota", "Add a note"),
        ("review.counter", "{n} de {max}", "{n} of {max}"),
        ("review.title", "Su revisión de esta regla", "Your review of this rule"),
        ("review.ruleTarget", "La regla completa", "The whole rule"),
        ("review.th.about", "Sobre", "About"),
        ("review.th.state", "Estado", "State"),
        ("review.th.note", "Nota", "Note"),
        ("review.unreviewed", "Sin revisar", "Not reviewed"),
        ("review.proposed", "Caso propuesto", "Proposed case"),
        ("review.noNote", "Sin nota", "No note"),
        ("review.case", "Caso «{name}»", "Case «{name}»"),
        ("review.proposedNote", "Se espera {expected}. Hoy la regla da {actual}.", "Expected: {expected}. Today the rule gives: {actual}."),
        ("review.rowsUnreviewed.one", "Fila {rows}", "Row {rows}"),
        ("review.rowsUnreviewed.other", "Filas {rows}", "Rows {rows}"),
        ("review.goRead", "Ir a leer", "Go to Read"),
        ("review.empty", "Todavía no ha revisado esta regla. Empiece en Leer.", "You have not reviewed this rule yet. Start in Read."),
        ("review.cardTitle", "Enviar la revisión", "Send the review"),
        ("review.summary.rows.one", "{n} de {t} fila revisada", "{n} of {t} row reviewed"),
        ("review.summary.rows.other", "{n} de {t} filas revisadas", "{n} of {t} rows reviewed"),
        ("review.summary.questions.one", "{n} pregunta", "{n} question"),
        ("review.summary.questions.other", "{n} preguntas", "{n} questions"),
        ("review.summary.fixes.one", "{n} por corregir", "{n} to fix"),
        ("review.summary.fixes.other", "{n} por corregir", "{n} to fix"),
        ("review.summary.cases.one", "{n} caso propuesto", "{n} proposed case"),
        ("review.summary.cases.other", "{n} casos propuestos", "{n} proposed cases"),
        ("review.scope.one", "Hay 1 regla con revisión en esta sesión.", "1 rule has a review in this session."),
        ("review.scope.other", "Hay {n} reglas con revisión en esta sesión.", "{n} rules have a review in this session."),
        ("review.name", "Su nombre", "Your name"),
        ("review.nameRequired", "Escriba su nombre para firmar la revisión.", "Enter your name to sign the review."),
        ("review.export", "Exportar la revisión", "Export the review"),
        ("review.exportHelp", "Se guarda un archivo en este equipo. Usted lo envía a quien mantiene las reglas. Exportar no cambia ni aprueba la regla.", "A file is saved on this computer. You send it to the rule's maintainer. Exporting does not change or approve the rule."),
        ("review.nothingToExport", "No hay nada que exportar.", "There is nothing to export."),
        ("review.saved", "Revisión guardada en {place}.", "Review saved to {place}."),
        ("review.savedNoPlace", "Revisión guardada.", "Review saved."),
        ("review.noteMissing", "Falta la nota", "Note missing"),
        ("review.writeNote", "Escribir la nota", "Write the note"),
        ("review.notesNeeded", "Escriba la nota de {n} elementos antes de exportar.", "Write the note for {n} items before exporting."),
        ("notice.privacy", "**No identifique a personas reales.** No escriba nombres, fechas de nacimiento, números de identificación ni otros datos de una persona real en las notas ni en los nombres de los casos. Use ejemplos inventados.", "**Do not identify real people.** Do not write names, dates of birth, identification numbers or any other detail of a real person in notes or case names. Use made-up examples."),

        ("band.simulate", "Simulado con datos de ejemplo. No es un caso real y no cambia nada.", "Simulated with example data. This is not a real case, and nothing changes."),
        ("band.tests", "Los casos de prueba son ejemplos inventados.", "Test cases are made-up examples."),
        ("pill.simulation", "Simulación", "Simulation"),

        ("sim.title", "Datos de ejemplo", "Example data"),
        ("sim.run", "Simular", "Simulate"),
        ("sim.clear", "Limpiar", "Clear"),
        ("sim.loadCase", "O cargue un caso de prueba", "Or load a test case"),
        ("sim.loadCaseNone", "Elija un caso…", "Choose a case…"),
        ("sim.before", "Escriba los datos de ejemplo y pulse Simular.", "Enter the example data and press Simulate."),
        ("sim.stale", "Los datos cambiaron. Simule de nuevo.", "The data changed. Simulate again."),
        ("sim.noValue", "Sin dato", "No value"),
        ("sim.yes", "Sí", "Yes"),
        ("sim.no", "No", "No"),
        ("sim.amount", "Monto", "Amount"),
        ("sim.currency", "Moneda de tres letras", "Three-letter currency"),
        ("sim.resultTitle", "Resultado: {values}", "Result: {values}"),
        ("sim.noDecision", "La regla no decide con estos datos", "The rule does not decide with this data"),
        ("sim.rowByRow", "Fila por fila", "Row by row"),
        ("sim.saveAsCase", "Guardar como caso de prueba", "Save as a test case"),
        ("sim.showAll", "Mostrar las {n} filas que no aplican", "Show the {n} rows that do not apply"),
        ("sim.composedNote", "Esta regla usa datos calculados por otras reglas, así que no hay explicación fila por fila.", "This rule uses data computed by other rules, so there is no row-by-row explanation."),
        ("sim.rowIf", "Si {conditions}…", "If {conditions}…"),
        ("sim.typed", "Usted puso {value}.", "You entered {value}."),
        ("sim.notTyped", "Usted no puso este dato.", "You did not enter this."),
        ("why.row", "Decidió la fila {n} porque {conditions}.", "Row {n} decided because {conditions}."),
        ("why.rows", "Decidieron las filas {rows}.", "Rows {rows} decided."),
        ("why.default", "Ninguna fila se cumplió, así que aplica el caso por defecto.", "No row matched, so the default case applies."),
        ("why.always", "aplica en todos los casos", "it applies in every case"),
        ("why.and", " y ", " and "),
        ("result.decidedRow", "Decidió la fila {n}.", "Row {n} decided."),
        ("result.default", "Decidió el caso por defecto.", "The default case decided."),
        ("result.none", "La regla no decide", "The rule does not decide"),
        ("row.decides", "Decide", "Decides"),
        ("row.matchesNoDecide", "Se cumple, no decide", "Matches, does not decide"),
        ("row.notApply", "No aplica", "Does not apply"),
        ("row.earlier", "Una fila anterior decidió primero.", "An earlier row decided first."),
        ("cond.met", "Se cumple", "Met"),
        ("cond.notMet", "No se cumple", "Not met"),

        ("hint.required", "Obligatorio.", "Required."),
        ("hint.code", "Texto.", "Text."),
        ("hint.integer", "Número entero.", "Whole number."),
        ("hint.decimal", "Número, con punto decimal.", "Number, with a decimal point."),
        ("hint.money", "Monto y moneda.", "Amount and currency."),
        ("hint.moneyUsd", "Monto en dólares.", "Amount in dollars."),
        ("err.required", "Falta este dato.", "This is required."),
        ("err.notAllowed", "Elija una de las opciones.", "Choose one of the options."),
        ("err.notBoolean", "Elija Sí o No.", "Choose Yes or No."),
        ("err.notInteger", "Escriba un número entero, por ejemplo 12.", "Enter a whole number, for example 12."),
        ("err.notDecimal", "Escriba un número, por ejemplo 7.5.", "Enter a number, for example 7.5."),
        ("err.notDate", "Elija una fecha.", "Choose a date."),
        ("err.notMoney", "Escriba un monto y una moneda de tres letras, por ejemplo 12.50 USD.", "Enter an amount and a three-letter currency, for example 12.50 USD."),
        ("err.generic", "Revise este dato.", "Check this value."),

        ("refuse.title", "No se pudo simular", "Could not simulate"),
        ("refuse.intro", "El motor de reglas no aceptó estos datos, así que no hay resultado.", "The rule engine did not accept this data, so there is no result."),
        ("refuse.missing", "Falta un dato obligatorio: {fact}.", "A required value is missing: {fact}."),
        ("refuse.typeMismatch", "El dato {fact} no tiene la forma que la regla espera.", "The value {fact} is not in the form the rule expects."),
        ("refuse.tooLarge", "Los datos son demasiado largos para esta regla.", "The data is too long for this rule."),
        ("refuse.multipleMatches", "Con estos datos se cumplen dos filas a la vez y la regla solo permite una. Esto es un defecto de la regla.", "Two rows match this data and the rule allows only one. This is a defect in the rule."),
        ("refuse.composition", "Esta regla depende de otras que no se pudieron calcular aquí.", "This rule depends on others that could not be computed here."),
        ("refuse.other", "El motor no pudo evaluar la regla con estos datos.", "The engine could not evaluate the rule with this data."),
        ("refuse.fixData", "Corrija el dato marcado y simule de nuevo.", "Correct the marked value and simulate again."),
        ("refuse.addToReview", "Anótelo en la revisión de esta regla.", "Note it in the review of this rule."),
        ("refuse.addButton", "Agregar a la revisión", "Add to the review"),

        ("tests.add", "Agregar un caso", "Add a case"),
        ("tests.runAll", "Probar todos", "Run all"),
        ("tests.running", "Probando…", "Running…"),
        ("tests.pass.one", "{p} de {t} caso pasa", "{p} of {t} case passes"),
        ("tests.pass.other", "{p} de {t} casos pasan", "{p} of {t} cases pass"),
        ("tests.th.case", "Caso", "Case"),
        ("tests.th.data", "Datos", "Data"),
        ("tests.th.expected", "Se espera", "Expected"),
        ("tests.th.actual", "La regla da", "The rule gives"),
        ("tests.th.status", "Estado", "Status"),
        ("tests.pass", "Pasa", "Passes"),
        ("tests.fail", "Falla", "Fails"),
        ("tests.proposed", "Propuesto por usted", "Proposed by you"),
        ("tests.openSim", "Ver en el simulador", "Open in the simulator"),
        ("tests.review", "Revisar", "Review"),
        ("tests.failNote", "**Un caso que falla no es un error suyo.** Significa que la regla y lo que usted espera no coinciden. El caso viaja con su revisión para que se corrija la regla o el caso.", "**A failing case is not your mistake.** It means the rule and what you expect do not agree. The case travels with your review so the rule or the case gets fixed."),
        ("tests.uncovered", "Filas sin caso de prueba", "Rows with no test case"),
        ("tests.createFor", "Crear un caso para esta fila", "Create a case for this row"),
        ("tests.allCovered", "Todas las filas tienen al menos un caso que pasa.", "Every row has at least one passing case."),
        ("tests.empty", "Esta regla no tiene casos de prueba. Agregue el primero desde el simulador.", "This rule has no test cases. Add the first one from the simulator."),
        ("case.name", "Nombre del caso", "Case name"),
        ("case.expected", "Resultado que se espera", "Expected result"),
        ("case.pick", "Elija un resultado…", "Choose a result…"),
        ("case.save", "Guardar caso", "Save case"),
        ("case.cancel", "Cancelar", "Cancel"),
        ("case.nameRequired", "Escriba un nombre para el caso.", "Enter a name for the case."),
        ("case.nameTaken", "Ya hay un caso con ese nombre.", "A case with that name already exists."),
        ("case.expectedRequired", "Elija el resultado que se espera.", "Choose the expected result."),
    ];

    private static readonly Dictionary<string, (string Es, string En)> ByKey = Table.ToDictionary(
        entry => entry.Key, entry => (entry.Es, entry.En), StringComparer.Ordinal);

    /// <summary>Every string key, in ordinal order.</summary>
    public static IReadOnlyList<string> Keys { get; } = [.. Table.Select(entry => entry.Key).Order(StringComparer.Ordinal)];

    /// <summary>The default text of a key in a locale.</summary>
    /// <param name="key">One of <see cref="Keys"/>.</param>
    /// <param name="locale">The language of the text.</param>
    /// <exception cref="ArgumentException">The key is not in <see cref="Keys"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The locale is not a defined value.</exception>
    public static string Default(string key, WorkbenchLocale locale)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!ByKey.TryGetValue(key, out var texts))
        {
            throw new ArgumentException("Unknown string key.", nameof(key));
        }

        return locale switch
        {
            WorkbenchLocale.Es => texts.Es,
            WorkbenchLocale.En => texts.En,
            _ => throw new ArgumentOutOfRangeException(nameof(locale), locale, "Unknown locale."),
        };
    }

    /// <summary>True for the markings a host may reword but never remove.</summary>
    internal static bool IsMandatory(string key) => MandatoryKeys.Contains(key, StringComparer.Ordinal);
}

namespace Synapbiz.Brain.Authoring;

/// <summary>The sentences that take the place of every text the schema validator, the engine or the platform would have written.</summary>
internal static partial class AuthoringText
{
    private static void AddPeople()
    {
        AddInputWords();
        AddCaseWords();
        AddEditWords();
    }

    private static void AddInputWords()
    {
        T("input.generic", "The request does not match what this tool accepts.", "La petición no coincide con lo que acepta esta herramienta.");
        T("input.too_deep", "A document is nested more deeply than the protocol allows. Make it flatter.", "Un documento está anidado a más profundidad de la que permite el protocolo. Hazlo menos profundo.");
        T("input.of", " of {container}", " de {container}");
        T("input.unknown", "{member} is not a valid member{of}; use {allowed}.", "{member} no es un miembro válido{of}; usa {allowed}.");
        T("input.unknown.none", "{member} is not a valid member{of}; nothing can be given there.", "{member} no es un miembro válido{of}; ahí no se puede dar nada.");
        T("input.missing", "The request needs {member}{of}.", "La petición necesita {member}{of}.");
        T("input.notobject", "The request must be a set of named values.", "La petición debe ser un conjunto de valores con nombre.");
        T("input.type", "{member} must be {kind}.", "{member} debe ser {kind}.");
        T("input.kind.text", "text", "texto");
        T("input.kind.whole", "a whole number", "un número entero");
        T("input.kind.number", "a number", "un número");
        T("input.kind.flag", "yes or no", "sí o no");
        T("input.kind.list", "a list", "una lista");
        T("input.kind.set", "a set of named values", "un conjunto de valores con nombre");
        T("input.kind.value", "a different kind of value", "otro tipo de valor");
        T("input.value", "{member} must be one of {allowed}.", "{member} debe ser uno de {allowed}.");
        T("input.pattern", "{member} is not written the way it must be: letters, digits, underscores, dots and hyphens only, starting with a letter, a digit or an underscore.", "{member} no está escrito como debe: solo letras, dígitos, guiones bajos, puntos y guiones, empezando por una letra, un dígito o un guion bajo.");
        T("input.long", "{member} is too long; it takes at most {max} characters.", "{member} es demasiado largo; admite como máximo {max} caracteres.");
        T("input.empty", "{member} cannot be empty.", "{member} no puede estar vacío.");
        T("input.many", "{member} has too many items; it takes at most {max}.", "{member} tiene demasiados elementos; admite como máximo {max}.");
        T("input.range", "{member} must be a whole number between {min} and {max}.", "{member} debe ser un número entero entre {min} y {max}.");
        T("input.member", "{member} cannot be used as it is.", "{member} no se puede usar tal como está.");

        T("patch.reason.not_object", "it is not a change with a kind and a place", "no es un cambio con un tipo y un lugar");
        T("patch.reason.no_path", "it does not say where to make the change", "no dice dónde hacer el cambio");
        T("patch.reason.no_value", "it gives no new value", "no da ningún valor nuevo");
        T("patch.reason.no_from", "it does not say where to take the value from", "no dice de dónde tomar el valor");
        T("patch.reason.unknown_op", "its kind of change is not one that is known", "su tipo de cambio no es de los que se conocen");
        T("patch.reason.into_itself", "a part cannot be moved into itself", "una parte no se puede mover dentro de sí misma");
        T("patch.reason.test_failed", "the value it checks is not the one it expects", "el valor que comprueba no es el que espera");
        T("patch.reason.missing", "the place it names is not there ({where})", "el lugar que nombra no existe ({where})");
        T("patch.reason.bad_pointer", "the place it names is not written correctly", "el lugar que nombra no está escrito correctamente");
        T("patch.reason.bad_position", "it names a position in a list that does not exist", "nombra una posición de una lista que no existe");
        T("patch.reason.whole_document", "the whole document cannot be removed or replaced by nothing", "no se puede quitar todo el documento ni reemplazarlo por nada");
        T("patch.reason.not_container", "the place where it works cannot hold a value", "el lugar donde actúa no puede contener un valor");
    }

    private static void AddCaseWords()
    {
        T("scenario.fail.misaddressed", "The test answers something at {step}, but the case is not waiting there.", "La prueba responde algo en {step}, pero el caso no espera ahí.");
        T("scenario.fail.refused", "The case did not accept what the test sent. {reason}", "El caso no aceptó lo que envió la prueba. {reason}");
        T("scenario.fail.noanswer", "The test gives no answer for {step}.", "La prueba no da ninguna respuesta para {step}.");
        T("scenario.fail.status", "The test expects the case to {expected}, but the case {actual}.", "La prueba espera que el caso {expected}, pero el caso {actual}.");
        T("status.expect.Completed", "finish", "termine");
        T("status.expect.Waiting", "keep waiting", "siga esperando");
        T("status.expect.Suspended", "stop at a problem", "se detenga por un problema");
        T("status.expect.Cancelled", "be cancelled", "se cancele");
        T("status.got.Completed", "finishes", "termina");
        T("status.got.Waiting", "keeps waiting", "sigue esperando");
        T("status.got.Suspended", "stops at a problem", "se detiene por un problema");
        T("status.got.Cancelled", "is cancelled", "se cancela");
        T("scenario.fail.result", "The test expects the case to end with the result {expected}, but it ends with {actual}.", "La prueba espera que el caso termine con el resultado {expected}, pero termina con {actual}.");
        T("scenario.fail.trace", "The test expects the case to go through other steps than the ones it goes through.", "La prueba espera que el caso pase por otros pasos distintos de los que recorre.");
        T("scenario.fail.variable", "The test expects the piece of information {name} to end with another value.", "La prueba espera que el dato {name} termine con otro valor.");
        T("scenario.fail.other", "The case does not behave as the test says.", "El caso no se comporta como dice la prueba.");
        T("scenario.notwritable.notstarted", "The case never started, so there is nothing to record as a test.", "El caso nunca empezó, así que no hay nada que registrar como prueba.");
        T("scenario.notwritable.name", "The name of the test cannot be used; give it a sentence of its own.", "El nombre de la prueba no se puede usar; dale una frase propia.");
        T("scenario.notwritable.other", "Something that happened in this simulation cannot be written in a test.", "Algo de lo que pasó en esta simulación no se puede escribir en una prueba.");

        T("start.at", "The start time must be written as a date and time with its zone, like 2027-01-04T14:00:00Z.", "La hora de inicio debe escribirse como fecha y hora con su zona, por ejemplo 2027-01-04T14:00:00Z.");
        T("start.today", "The date of the case must be written year-month-day, like 2027-01-04.", "La fecha del caso debe escribirse año-mes-día, por ejemplo 2027-01-04.");
        T("start.zone", "The time zone offset for {zone} must be written like +01:00 or -05:00, at most 14 hours.", "El desfase de la zona horaria {zone} debe escribirse como +01:00 o -05:00, de 14 horas como máximo.");
        T("start.other", "A time zone given with the start cannot be used: it needs a name the tests accept and an offset of at most 14 hours.", "Una zona horaria dada con el inicio no se puede usar: necesita un nombre que las pruebas acepten y un desfase de 14 horas como máximo.");
        T("start.extra", "The process does not take {names} when a case starts. It takes {allowed}.", "El proceso no recibe {names} al empezar un caso. Recibe {allowed}.");
        T("start.noinputs", "nothing", "nada");
        T("start.type", "The value given for {name} is not of the kind the process expects, {kind}.", "El valor dado para {name} no es del tipo que el proceso espera, {kind}.");
        T("vartype.code", "a code", "un código");
        T("vartype.enum", "one of its allowed values", "uno de sus valores permitidos");
        T("vartype.boolean", "yes or no", "sí o no");
        T("vartype.integer", "a whole number", "un número entero");
        T("vartype.decimal", "a number with decimals", "un número con decimales");
        T("vartype.money", "an amount of money with its currency", "un importe con su moneda");
        T("vartype.date", "a date written year-month-day", "una fecha escrita año-mes-día");
        T("vartype.reference", "a reference", "una referencia");
        T("vartype.instant", "a date and time with its zone", "una fecha y hora con su zona");
        T("vartype.json", "a set of named values", "un conjunto de valores con nombre");

        T("command.needtarget", "Say which of the things the case waits for this answers.", "Indica a cuál de las cosas que espera el caso responde esto.");
        T("command.ambiguous", "More than one thing the case waits for matches; say which one.", "Coincide más de una cosa que espera el caso; indica cuál.");
        T("command.notwaiting", "The case is not waiting for that. It waits for {waits}.", "El caso no espera eso. Espera {waits}.");
        T("command.needobject", "The command needs {member} as a set of named values.", "El comando necesita {member} como un conjunto de valores con nombre.");
        T("command.needmember", "The command needs {member}.", "El comando necesita {member}.");
        T("command.duration", "The length of time must be written like PT1H for one hour or P2D for two days.", "La duración debe escribirse como PT1H para una hora o P2D para dos días.");
        T("command.other", "The command cannot be carried out as it is given.", "El comando no se puede llevar a cabo tal como se da.");
    }

    private static void AddEditWords()
    {
        T("edit.locale", "The language of a label must be English or Spanish.", "El idioma de un nombre debe ser inglés o español.");
        T("edit.operation", "The kind of change is not one that is known.", "El tipo de cambio no es de los que se conocen.");
        T("edit.unknownfact", "The rule does not read {name}.", "La regla no lee {name}.");
        T("edit.needtext", "The change needs {member} written as text.", "El cambio necesita {member} escrito como texto.");
        T("edit.needoperator", "A condition needs the way it compares.", "Una condición necesita la forma en que compara.");
        T("edit.needtextvalue", "The condition on {name} needs a text to compare with.", "La condición sobre {name} necesita un texto con el que comparar.");
        T("edit.unknownoperator", "The way the condition on {name} compares is not one that is known.", "La forma en que compara la condición sobre {name} no es de las que se conocen.");
        T("edit.needrange", "A range on {name} needs a lower end, an upper end, or both.", "Un rango sobre {name} necesita un extremo inferior, uno superior, o ambos.");
        T("edit.needvalue", "The condition on {name} needs a value.", "La condición sobre {name} necesita un valor.");
        T("edit.needvalues", "The condition needs a list with at least one value.", "La condición necesita una lista con al menos un valor.");
        T("edit.unknownfield", "The rule gives no result called {name}.", "La regla no da ningún resultado llamado {name}.");
        T("edit.needscalar", "A value must be text, a number, or yes or no.", "Un valor debe ser texto, un número, o sí o no.");
        T("edit.other", "That change cannot be used as it is written.", "Ese cambio no se puede usar tal como está escrito.");
        T("facts.unknown", "The rule does not read {names}. It reads {known}.", "La regla no lee {names}. Lee {known}.");
        T("facts.asof", "The date must be written year-month-day, like 2027-01-04.", "La fecha debe escribirse año-mes-día, por ejemplo 2027-01-04.");
    }
}

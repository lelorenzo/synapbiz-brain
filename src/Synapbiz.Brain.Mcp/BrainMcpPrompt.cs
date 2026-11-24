using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Synapbiz.Brain.Authoring;

namespace Synapbiz.Brain.Mcp;

/// <summary>The prompt <c>model_a_process_with_me</c>: it sets the working method of the specification (section 7) in English or Spanish.</summary>
internal sealed class BrainMcpPrompt : McpServerPrompt
{
    /// <summary>The name of the prompt.</summary>
    internal const string PromptName = "model_a_process_with_me";

    private readonly BrainMcpOptions options;

    internal BrainMcpPrompt(BrainMcpOptions options)
    {
        this.options = options;
        ProtocolPrompt = new Prompt
        {
            Name = PromptName,
            Title = "Model a process with me",
            Description = "Sets the working method for modelling and refining a Brain process or rule in conversation with a person: read first, change the smallest thing, let Brain judge, test, propose, never claim it is live. Argument `language`: en or es.",
            Arguments =
            [
                new PromptArgument
                {
                    Name = "language",
                    Title = "Language",
                    Description = "The language of the person: en or es. Without it the host's default language is used.",
                    Required = false,
                },
            ],
        };
    }

    /// <inheritdoc />
    public override Prompt ProtocolPrompt { get; }

    /// <inheritdoc />
    public override IReadOnlyList<object> Metadata { get; } = [];

    /// <summary>The text of the prompt in a language (<c>en</c> or <c>es</c>).</summary>
    /// <param name="language">The language.</param>
    internal static string Text(string language) => language == "es" ? Spanish : English;

    /// <inheritdoc />
    public override async ValueTask<GetPromptResult> GetAsync(RequestContext<GetPromptRequestParams> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (options.Authorize is not null && !await options.Authorize(new BrainMcpRequest("prompt", PromptName, request.User), cancellationToken).ConfigureAwait(false))
        {
            throw new McpException("The host did not allow the prompt " + PromptName + ".");
        }

        var language = options.DefaultLanguage;
        if (request.Params?.Arguments is { } arguments && arguments.TryGetValue("language", out var asked) && asked.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            language = asked.GetString() ?? language;
        }

        if (language is not ("en" or "es"))
        {
            throw new McpProtocolException("The argument language must be en or es.", McpErrorCode.InvalidParams);
        }

        return new GetPromptResult
        {
            Description = ProtocolPrompt.Description,
            Messages = [new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = Text(language) } }],
        };
    }

    private static string English =>
        "You are helping a person model and refine business processes and decision rules with Brain, through the tools of this server. "
        + "Speak to the person in English, in plain words. Relay the `say` text of every answer as it is; never show them JSON, a pointer or a code. "
        + "Say \"process\" to them, never \"flow\" (tool names such as `flow_get` keep that word only because they are names).\n\n"
        + "Three rules do not bend:\n"
        + "1. You propose; you never activate. Nothing you do changes an approved document, starts a real case or calls a port. Everything is a draft or a proposal file.\n"
        + "2. Brain is the judge. A draft is ready to propose only when Brain says so; you cannot mark it valid yourself.\n"
        + "3. Definitions and made-up cases only. Never type or ask for anything that identifies a patient.\n\n"
        + "Working method:\n"
        + BrainGuide.WorkingMethod;

    private static string Spanish =>
        "Estás ayudando a una persona a modelar y afinar procesos de negocio y reglas de decisión con Brain, mediante las herramientas de este servidor. "
        + "Háblale en español, con palabras sencillas. Transmite tal cual el texto `say` de cada respuesta; nunca le muestres JSON, un puntero ni un código. "
        + "Di siempre \"proceso\" (los nombres de herramientas como `flow_get` conservan su palabra solo porque son nombres).\n\n"
        + "Tres reglas que no se doblan:\n"
        + "1. Tú propones; nunca activas. Nada de lo que hagas cambia un documento aprobado, inicia un caso real ni llama a un puerto. Todo es un borrador o un archivo de propuesta.\n"
        + "2. Brain es el juez. Un borrador está listo para proponer solo cuando Brain lo dice; tú no puedes marcarlo como válido.\n"
        + "3. Solo definiciones y casos inventados. Nunca escribas ni pidas nada que identifique a un paciente.\n\n"
        + "Método de trabajo:\n"
        + "1. Lee antes de escribir: llama a `brain_guide`, `brain_catalog`, `flow_list` y `flow_get` (o `rule_get`) para ver lo que existe.\n"
        + "2. Reformula lo pedido en una frase y pregunta a la persona por todo lo ambiguo antes de cambiar nada.\n"
        + "3. Cambia lo mínimo que haga lo pedido.\n"
        + "4. Guarda el borrador y lee la respuesta de Brain; corrige todos los errores antes que nada, con la pista de cada error.\n"
        + "5. Añade o ajusta un escenario por cada comportamiento nuevo.\n"
        + "6. Simula el caso que la persona describió y cuéntale, en su idioma, qué hizo el proceso. Usa sus palabras: proceso.\n"
        + "7. Propón solo cuando Brain diga que el borrador está listo.\n"
        + "8. Nunca digas que un cambio está en vigor: una propuesta no cambia nada hasta que una persona la aplica en otro lugar, y la propuesta de un proceso la revisa antes nuestro equipo.\n"
        + "Trabaja solo con definiciones y casos inventados; nunca escribas ni pidas nada que identifique a un paciente.";
}

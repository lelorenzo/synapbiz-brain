namespace Synapbiz.Brain.Authoring;

/// <summary>
/// Something a tool cannot use, as the short code that picks its sentence, the name it is about (or null) and the values for the sentence.
/// The text of the exception is for the assistant; only the code, the name and the values reach a person.
/// </summary>
public sealed class AuthoringProblemException : Exception
{
    public AuthoringProblemException()
    {
        Code = "other";
    }

    public AuthoringProblemException(string message)
        : base(message)
    {
        Code = message;
    }

    public AuthoringProblemException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = message;
    }

    public AuthoringProblemException(string code, string? name, string message, params (string, string)[] values)
        : base(message)
    {
        Code = code;
        Name = name;
        Values = [.. values];
    }

    public string Code { get; }

    public string? Name { get; }

    public IReadOnlyList<(string, string)> Values { get; } = [];
}

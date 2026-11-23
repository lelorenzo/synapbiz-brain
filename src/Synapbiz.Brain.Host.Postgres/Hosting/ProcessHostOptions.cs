using Microsoft.Extensions.Options;

namespace Synapbiz.Brain.Process.Hosting;

/// <summary>The settings of the pump and the worker.</summary>
public sealed class ProcessHostOptions
{
    /// <summary>How long the worker waits after a pass that handled nothing.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How many items of each kind one pass handles, from 1 to 1000.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>How many port calls run at once in one pump, from 1 to 256.</summary>
    public int MaxConcurrentEffects { get; set; } = 8;

    /// <summary>How long a port call may take before the host gives up on it.</summary>
    public TimeSpan PortTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>How long a claimed effect stays claimed; must be longer than <see cref="PortTimeout"/>.</summary>
    public TimeSpan EffectLease { get; set; } = TimeSpan.FromSeconds(120);
}

/// <summary>Validates <see cref="ProcessHostOptions"/> when they are first resolved.</summary>
internal sealed class ProcessHostOptionsValidator : IValidateOptions<ProcessHostOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ProcessHostOptions options)
    {
        var failures = new List<string>();
        if (options.PollInterval <= TimeSpan.Zero)
        {
            failures.Add("ProcessHostOptions.PollInterval must be greater than zero.");
        }

        if (options.BatchSize is < 1 or > 1000)
        {
            failures.Add("ProcessHostOptions.BatchSize must be between 1 and 1000.");
        }

        if (options.MaxConcurrentEffects is < 1 or > 256)
        {
            failures.Add("ProcessHostOptions.MaxConcurrentEffects must be between 1 and 256.");
        }

        if (options.PortTimeout <= TimeSpan.Zero)
        {
            failures.Add("ProcessHostOptions.PortTimeout must be greater than zero.");
        }

        if (options.EffectLease <= options.PortTimeout)
        {
            failures.Add("ProcessHostOptions.EffectLease must be greater than PortTimeout.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

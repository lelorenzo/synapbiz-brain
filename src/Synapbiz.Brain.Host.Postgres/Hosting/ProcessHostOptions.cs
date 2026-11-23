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

    /// <summary>
    /// How late a schedule occurrence may be handled and still count as on time, from zero. An occurrence handled later than this
    /// has been missed (the host was down or late), and the misfire policy of its schedule decides what happens. At the boundary
    /// (exactly this late) the occurrence is on time.
    /// </summary>
    public TimeSpan ScheduleMisfireGrace { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The longest <see cref="IProcessEngine.PurgeInstanceAsync"/> keeps trying to lock a whole tree that another transaction holds,
    /// from just above zero to five minutes. A purge never waits for a row lock: it tries, gives the locks back and tries again after
    /// a short randomized pause. When this time has passed since the call began the result is <see cref="PurgeStatus.Refused"/> with
    /// the code <c>process.purge.busy</c> and nothing has changed.
    /// </summary>
    public TimeSpan PurgeLockTimeout { get; set; } = TimeSpan.FromSeconds(10);
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

        if (options.ScheduleMisfireGrace < TimeSpan.Zero)
        {
            failures.Add("ProcessHostOptions.ScheduleMisfireGrace must not be negative.");
        }

        if (options.PurgeLockTimeout <= TimeSpan.Zero || options.PurgeLockTimeout > TimeSpan.FromMinutes(5))
        {
            failures.Add("ProcessHostOptions.PurgeLockTimeout must be greater than zero and at most 5 minutes.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Synapbiz.Brain.Process.Hosting;

/// <summary>
/// The hosted worker: calls the pump in a loop. After a pass that handled work it runs again at once, otherwise it waits the poll
/// interval. A pass that fails is logged (the exception type only) and the loop continues after the poll interval.
/// </summary>
internal sealed class ProcessWorker : BackgroundService
{
    private readonly IProcessPump pump;
    private readonly ProcessHostOptions options;
    private readonly TimeProvider time;
    private readonly ILogger<ProcessWorker> logger;

    public ProcessWorker(IProcessPump pump, IOptions<ProcessHostOptions> options, TimeProvider time, ILogger<ProcessWorker> logger)
    {
        this.pump = pump;
        this.options = options.Value;
        this.time = time;
        this.logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        HostLog.WorkerStarted(logger, options.PollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            var pass = pump.RunOnceAsync(stoppingToken);
            await Task.WhenAny(pass).ConfigureAwait(false);
            if (pass.IsCompletedSuccessfully && await pass.ConfigureAwait(false) > 0)
            {
                continue;
            }

            if (pass.IsFaulted && pass.Exception!.GetBaseException() is { } failure)
            {
                HostLog.PassFailed(logger, failure.GetType().FullName ?? "Exception");
            }

            await Task.WhenAny(Task.Delay(options.PollInterval, time, stoppingToken)).ConfigureAwait(false);
        }
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres;

/// <summary>Registration of the durable process host.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IProcessEngine"/>, <see cref="IProcessPump"/>, the hosted worker, the PostgreSQL store, the migrator
    /// and the default <see cref="IProcessCalendar"/> (the UTC date). The consumer registers an <see cref="IProcessCatalog"/>, its
    /// <see cref="IProcessPort"/> implementations and an <see cref="IPayloadProtector"/> (or calls
    /// <see cref="AddPlaintextProcessPayloads"/>). Time comes from the registered <see cref="TimeProvider"/>, by default
    /// <see cref="TimeProvider.System"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureStore">Sets the connection string and the schema.</param>
    /// <param name="configureHost">Sets the options of the pump and the worker.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddBrainProcessHostPostgres(
        this IServiceCollection services,
        Action<PostgresProcessStoreOptions> configureStore,
        Action<ProcessHostOptions>? configureHost = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureStore);

        services.AddOptions<PostgresProcessStoreOptions>().Configure(configureStore);
        var host = services.AddOptions<ProcessHostOptions>();
        if (configureHost is not null)
        {
            host.Configure(configureHost);
        }

        if (services.Any(descriptor => descriptor.ServiceType == typeof(IProcessEngine)))
        {
            return services;
        }

        services.AddSingleton<IValidateOptions<PostgresProcessStoreOptions>>(new PostgresProcessStoreOptionsValidator());
        services.AddSingleton<IValidateOptions<ProcessHostOptions>>(new ProcessHostOptionsValidator());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IProcessCalendar>(new UtcProcessCalendar());
        services.TryAddSingleton<IZoneResolver>(new SystemZoneResolver());

        services.AddSingleton(provider => new PostgresProcessStore(provider.GetRequiredService<IOptions<PostgresProcessStoreOptions>>().Value));
        services.AddSingleton<IProcessStore>(provider => provider.GetRequiredService<PostgresProcessStore>());
        services.AddSingleton<IProcessStoreMigrator>(provider => new PostgresProcessStoreMigrator(
            provider.GetRequiredService<IOptions<PostgresProcessStoreOptions>>().Value,
            provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton(provider => new ProcessRuntime(
            provider.GetRequiredService<IProcessCatalog>(),
            provider.GetService<IPayloadProtector>() ?? throw new InvalidOperationException(
                "No IPayloadProtector is registered. Register your own implementation, or call AddPlaintextProcessPayloads() to store payloads as they are."),
            provider.GetRequiredService<IProcessCalendar>(),
            provider.GetRequiredService<IZoneResolver>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<ProcessRuntime>>()));
        services.AddSingleton<IProcessEngine>(provider => new ProcessEngine(provider.GetRequiredService<IProcessStore>(), provider.GetRequiredService<ProcessRuntime>()));
        services.AddSingleton<IProcessPump>(provider => new ProcessPump(
            provider.GetRequiredService<IProcessStore>(),
            provider.GetRequiredService<ProcessRuntime>(),
            provider.GetServices<IProcessPort>(),
            provider.GetRequiredService<IOptions<ProcessHostOptions>>()));
        services.AddSingleton<IHostedService>(provider => new ProcessWorker(
            provider.GetRequiredService<IProcessPump>(),
            provider.GetRequiredService<IOptions<ProcessHostOptions>>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<ProcessWorker>>()));
        return services;
    }

    /// <summary>
    /// Registers a pass-through <see cref="IPayloadProtector"/> that stores payloads as they are. For development and tests only.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddPlaintextProcessPayloads(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IPayloadProtector>(new PlaintextPayloadProtector());
        return services;
    }
}

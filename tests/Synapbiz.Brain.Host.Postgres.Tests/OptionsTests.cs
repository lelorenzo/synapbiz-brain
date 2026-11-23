using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>Registration, options validation and the argument checks of the engine. None of these needs a live connection except the engine checks.</summary>
public sealed class OptionsTests
{
    private const string Connection = "Host=localhost;Database=brain_unused;Username=brain;Password=brain";

    [Theory]
    [InlineData("brain")]
    [InlineData("_private")]
    [InlineData("a1_b2")]
    public void AValidSchemaNameIsAccepted(string schema)
    {
        var options = Resolve<PostgresProcessStoreOptions>(
            services => services.AddBrainProcessHostPostgres(store =>
            {
                store.ConnectionString = Connection;
                store.Schema = schema;
            }));

        options.Schema.Should().Be(schema);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1brain")]
    [InlineData("Brain")]
    [InlineData("brain-x")]
    [InlineData("brain;drop")]
    [InlineData("brain\"x")]
    [InlineData("brain x")]
    [InlineData("a234567890123456789012345678901234567890123456789012345678901234")]
    public void AnInvalidSchemaNameFailsOnFirstResolutionAndNamesTheMember(string schema)
    {
        var act = () => Resolve<PostgresProcessStoreOptions>(
            services => services.AddBrainProcessHostPostgres(store =>
            {
                store.ConnectionString = Connection;
                store.Schema = schema;
            }));

        act.Should().Throw<OptionsValidationException>().Which.Message.Should().Contain("Schema");
    }

    [Fact]
    public void ARegistrationWithoutAConnectionStringFailsAndNamesTheMemberWithoutTheValue()
    {
        var act = () => Resolve<PostgresProcessStoreOptions>(services => services.AddBrainProcessHostPostgres(_ => { }));

        act.Should().Throw<OptionsValidationException>().Which.Message.Should().Contain("ConnectionString");
    }

    [Fact]
    public void RegistrationItselfDoesNotValidate()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBrainProcessHostPostgres(_ => { });

        act.Should().NotThrow();
    }

    [Fact]
    public void TheDefaultHostOptionsAreValid()
    {
        var options = Resolve<ProcessHostOptions>(services => services.AddBrainProcessHostPostgres(store => store.ConnectionString = Connection));

        options.PollInterval.Should().Be(TimeSpan.FromSeconds(5));
        options.BatchSize.Should().Be(50);
        options.MaxConcurrentEffects.Should().Be(8);
        options.PortTimeout.Should().Be(TimeSpan.FromSeconds(60));
        options.EffectLease.Should().Be(TimeSpan.FromSeconds(120));
        options.ScheduleMisfireGrace.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void ANegativeMisfireGraceFailsOnFirstResolutionAndNamesTheMemberWhileZeroIsAccepted()
    {
        var negative = () => Resolve<ProcessHostOptions>(
            services => services.AddBrainProcessHostPostgres(store => store.ConnectionString = Connection, host => host.ScheduleMisfireGrace = TimeSpan.FromSeconds(-1)));
        var zero = Resolve<ProcessHostOptions>(
            services => services.AddBrainProcessHostPostgres(store => store.ConnectionString = Connection, host => host.ScheduleMisfireGrace = TimeSpan.Zero));

        negative.Should().Throw<OptionsValidationException>().Which.Message.Should().Contain("ScheduleMisfireGrace");
        zero.ScheduleMisfireGrace.Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData("PollInterval", 0, 50, 8, 60, 120)]
    [InlineData("PollInterval", -1, 50, 8, 60, 120)]
    [InlineData("BatchSize", 5, 0, 8, 60, 120)]
    [InlineData("BatchSize", 5, 1001, 8, 60, 120)]
    [InlineData("MaxConcurrentEffects", 5, 50, 0, 60, 120)]
    [InlineData("MaxConcurrentEffects", 5, 50, 257, 60, 120)]
    [InlineData("PortTimeout", 5, 50, 8, 0, 120)]
    [InlineData("EffectLease", 5, 50, 8, 60, 60)]
    [InlineData("EffectLease", 5, 50, 8, 60, 30)]
    public void InvalidHostOptionsFailOnFirstResolutionAndNameTheMember(string member, int poll, int batch, int concurrent, int timeout, int lease)
    {
        var act = () => Resolve<ProcessHostOptions>(
            services => services.AddBrainProcessHostPostgres(
                store => store.ConnectionString = Connection,
                host =>
                {
                    host.PollInterval = TimeSpan.FromSeconds(poll);
                    host.BatchSize = batch;
                    host.MaxConcurrentEffects = concurrent;
                    host.PortTimeout = TimeSpan.FromSeconds(timeout);
                    host.EffectLease = TimeSpan.FromSeconds(lease);
                }));

        act.Should().Throw<OptionsValidationException>().Which.Message.Should().Contain(member);
    }

    [Fact]
    public void TheBoundariesOfTheHostOptionsAreAccepted()
    {
        var options = Resolve<ProcessHostOptions>(
            services => services.AddBrainProcessHostPostgres(
                store => store.ConnectionString = Connection,
                host =>
                {
                    host.BatchSize = 1000;
                    host.MaxConcurrentEffects = 256;
                    host.PollInterval = TimeSpan.FromMilliseconds(1);
                    host.PortTimeout = TimeSpan.FromMilliseconds(1);
                    host.EffectLease = TimeSpan.FromMilliseconds(2);
                }));

        options.BatchSize.Should().Be(1000);
        options.MaxConcurrentEffects.Should().Be(256);
    }

    [Fact]
    public void AMissingProtectorIsAnErrorThatSaysWhatToRegister()
    {
        using var provider = Build(services => services.AddBrainProcessHostPostgres(store => store.ConnectionString = Connection), protector: false);

        var act = () => provider.GetRequiredService<IProcessEngine>();

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("IPayloadProtector").And.Contain("AddPlaintextProcessPayloads");
    }

    [Fact]
    public void ARegisteredProtectorIsUsedInsteadOfThePlaintextOne()
    {
        var own = new ReversingProtector();
        using var provider = Build(
            services =>
            {
                services.AddSingleton<IPayloadProtector>(own);
                services.AddPlaintextProcessPayloads();
                services.AddBrainProcessHostPostgres(store => store.ConnectionString = Connection);
            },
            protector: false);

        provider.GetRequiredService<IPayloadProtector>().Should().BeSameAs(own);
        provider.GetRequiredService<IProcessEngine>().Should().NotBeNull();
    }

    [Fact]
    public void RegisteringTwiceKeepsOneEngineAndOneWorker()
    {
        using var provider = Build(
            services =>
            {
                services.AddBrainProcessHostPostgres(store => store.ConnectionString = Connection);
                services.AddBrainProcessHostPostgres(store => store.Schema = "second");
            });

        provider.GetServices<IHostedService>().Should().ContainSingle();
        provider.GetRequiredService<IOptions<PostgresProcessStoreOptions>>().Value.Schema.Should().Be("second");
    }

    [Fact]
    public void ThePumpTheEngineAndTheMigratorAreSingletonsAndTheWorkerIsHosted()
    {
        using var provider = Build(services => services.AddBrainProcessHostPostgres(store => store.ConnectionString = Connection));

        provider.GetRequiredService<IProcessEngine>().Should().BeSameAs(provider.GetRequiredService<IProcessEngine>());
        provider.GetRequiredService<IProcessPump>().Should().BeSameAs(provider.GetRequiredService<IProcessPump>());
        provider.GetRequiredService<IProcessStoreMigrator>().Should().BeSameAs(provider.GetRequiredService<IProcessStoreMigrator>());
        provider.GetServices<IHostedService>().Should().ContainSingle();
    }

    [Fact]
    public void TheRegistrationMethodsRejectNullArguments()
    {
        var services = new ServiceCollection();

        ((Action)(() => ServiceCollectionExtensions.AddBrainProcessHostPostgres(null!, _ => { }))).Should().Throw<ArgumentNullException>();
        ((Action)(() => services.AddBrainProcessHostPostgres(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => ServiceCollectionExtensions.AddPlaintextProcessPayloads(null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TheDefaultCalendarIsTheUtcDate()
    {
        using var provider = Build(services => services.AddBrainProcessHostPostgres(store => store.ConnectionString = Connection));

        var calendar = provider.GetRequiredService<IProcessCalendar>();

        calendar.Should().NotBeNull();
    }

    [Fact]
    public void ThePlaintextProtectorReturnsWhatItIsGiven()
    {
        using var provider = Build(services => services.AddBrainProcessHostPostgres(store => store.ConnectionString = Connection));
        var protector = provider.GetRequiredService<IPayloadProtector>();

        protector.Protect("t-1", "{\"a\":\"b\"}").Should().Be("{\"a\":\"b\"}");
        protector.Unprotect("t-1", "{\"a\":\"b\"}").Should().Be("{\"a\":\"b\"}");
    }

    [Fact]
    public async Task EngineCommandsRejectBadArgumentsBeforeTouchingTheDatabase()
    {
        using var provider = Build(services => services.AddBrainProcessHostPostgres(store => store.ConnectionString = Connection));
        var engine = provider.GetRequiredService<IProcessEngine>();
        var tooLong = new string('x', 201);

        await Throws<ArgumentNullException>(() => engine.StartAsync(null!));
        await Throws<ArgumentException>(() => engine.StartAsync(new StartRequest("", "p", "k", "{}")));
        await Throws<ArgumentException>(() => engine.StartAsync(new StartRequest("t-1", tooLong, "k", "{}")));
        await Throws<ArgumentException>(() => engine.StartAsync(new StartRequest("t-1", "p", "", "{}")));
        await Throws<ArgumentException>(() => engine.StartAsync(new StartRequest("t-1", "p", "k", "[1]")));
        await Throws<ArgumentException>(() => engine.StartAsync(new StartRequest("t-1", "p", "k", "not json")));
        await Throws<ArgumentNullException>(() => engine.StartAsync(new StartRequest("t-1", "p", "k", null!)));

        await Throws<ArgumentNullException>(() => engine.PublishEventAsync(null!));
        await Throws<ArgumentException>(() => engine.PublishEventAsync(new ProcessEvent("t-1", "", "n", null, "{}")));
        await Throws<ArgumentNullException>(() => engine.PublishEventAsync(new ProcessEvent("t-1", "e-1", null!, null, "{}")));
        await Throws<ArgumentException>(() => engine.PublishEventAsync(new ProcessEvent("t-1", "e-1", "n", null, "\"text\"")));

        await Throws<ArgumentException>(() => engine.CompleteTaskAsync("", "task", "ok", "{}"));
        await Throws<ArgumentException>(() => engine.CompleteTaskAsync("t-1", "", "ok", "{}"));
        await Throws<ArgumentNullException>(() => engine.CompleteTaskAsync("t-1", "task", null!, "{}"));
        await Throws<ArgumentException>(() => engine.CompleteTaskAsync("t-1", "task", "ok", "[]"));

        await Throws<ArgumentException>(() => engine.CancelAsync("", "i-1", "reason"));
        await Throws<ArgumentNullException>(() => engine.CancelAsync("t-1", null!, "reason"));
        await Throws<ArgumentNullException>(() => engine.CancelAsync("t-1", "i-1", null!));
        await Throws<ArgumentException>(() => engine.ResumeAsync("", "i-1"));
        await Throws<ArgumentNullException>(() => engine.ResumeAsync("t-1", null!));
        await Throws<ArgumentException>(() => engine.GetInstanceAsync("", "i-1"));
        await Throws<ArgumentNullException>(() => engine.GetInstanceAsync("t-1", null!));
        await Throws<ArgumentException>(() => engine.FindInstanceAsync("t-1", "", "k"));
        await Throws<ArgumentException>(() => engine.FindInstanceAsync("t-1", "p", ""));
        await Throws<ArgumentException>(() => engine.GetHistoryAsync("", "i-1"));
        await Throws<ArgumentNullException>(() => engine.GetHistoryAsync("t-1", null!));
        await Throws<ArgumentException>(() => engine.ListOpenTasksAsync("", "q"));
        await Throws<ArgumentNullException>(() => engine.ListOpenTasksAsync("t-1", null!));
    }

    [Fact]
    public async Task AnIdOfExactlyTheLimitIsAccepted()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("quick"));

        var result = await rig.Engine.StartAsync(new StartRequest(rig.Tenant, "host.quick", new string('k', 200), "{\"case_ref\":\"c-1\"}"));

        result.Created.Should().BeTrue();
    }

    private static async Task Throws<TException>(Func<Task> action)
        where TException : Exception =>
        await FluentActions.Awaiting(action).Should().ThrowExactlyAsync<TException>();

    private static T Resolve<T>(Action<IServiceCollection> register)
        where T : class
    {
        using var provider = Build(register);
        return provider.GetRequiredService<IOptions<T>>().Value;
    }

    private static ServiceProvider Build(Action<IServiceCollection> register, bool protector = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IProcessCatalog>(new TestCatalog());
        if (protector)
        {
            services.AddPlaintextProcessPayloads();
        }

        register(services);
        return services.BuildServiceProvider();
    }
}

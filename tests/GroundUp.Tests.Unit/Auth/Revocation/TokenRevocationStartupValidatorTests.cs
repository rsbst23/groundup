using GroundUp.Auth.Core.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Auth.Services.Revocation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Revocation;

public sealed class TokenRevocationStartupValidatorTests
{
    private readonly CapturingLogger _logger = new();

    private TokenRevocationStartupValidator Create(string environment, ITokenRevocationStore store, bool allow = false)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(environment);
        return new TokenRevocationStartupValidator(
            store, env, Options.Create(new AuthOptions { AllowInMemoryTokenRevocationStore = allow }), _logger);
    }

    private static InMemoryTokenRevocationStore InMemory() => new(TimeProvider.System);

    [Fact]
    public async Task StartAsync_ProductionWithInMemoryStore_Throws()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Create(Environments.Production, InMemory()).StartAsync(CancellationToken.None));

        Assert.Contains("GroundUp:Auth:AllowInMemoryTokenRevocationStore", ex.Message);
        Assert.Contains("per process", ex.Message);
    }

    [Fact]
    public async Task StartAsync_ProductionWithInMemoryStoreAndOptIn_StartsAndWarns()
    {
        await Create(Environments.Production, InMemory(), allow: true).StartAsync(CancellationToken.None);

        Assert.Equal(new[] { LogLevel.Warning }, _logger.Levels);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public async Task StartAsync_OtherEnvironments_StartsSilently(string environment)
    {
        await Create(environment, InMemory()).StartAsync(CancellationToken.None);

        Assert.Empty(_logger.Levels);
    }

    [Fact]
    public async Task StartAsync_ProductionWithCustomStore_Starts()
    {
        await Create(Environments.Production, Substitute.For<ITokenRevocationStore>()).StartAsync(CancellationToken.None);
    }

    [Fact]
    public void AddGroundUpAuth_RegistersInMemoryStoreAsSingletonAndTheValidator()
    {
        var services = new ServiceCollection();

        services.AddGroundUpAuth(_ => { });

        var store = Assert.Single(services, d => d.ServiceType == typeof(ITokenRevocationStore));
        Assert.Equal(ServiceLifetime.Singleton, store.Lifetime);
        Assert.Contains(services, d => d.ImplementationType == typeof(TokenRevocationStartupValidator));

        using var provider = services.BuildServiceProvider();
        Assert.IsType<InMemoryTokenRevocationStore>(provider.GetRequiredService<ITokenRevocationStore>());
    }

    [Fact]
    public void AddGroundUpAuth_KeepsStoreRegisteredByTheApplication()
    {
        var services = new ServiceCollection();
        var custom = Substitute.For<ITokenRevocationStore>();
        services.AddSingleton(custom);

        services.AddGroundUpAuth(_ => { });

        using var provider = services.BuildServiceProvider();
        Assert.Same(custom, provider.GetRequiredService<ITokenRevocationStore>());
    }

    private sealed class CapturingLogger : ILogger<TokenRevocationStartupValidator>
    {
        public List<LogLevel> Levels { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Levels.Add(logLevel);
    }
}

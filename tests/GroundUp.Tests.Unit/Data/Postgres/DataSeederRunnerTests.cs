using GroundUp.Data.Abstractions;
using GroundUp.Data.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GroundUp.Tests.Unit.Data.Postgres;

/// <summary>
/// Unit tests for <see cref="DataSeederRunner"/>.
/// </summary>
public sealed class DataSeederRunnerTests
{
    [Fact]
    public async Task StartAsync_MultipleSeeders_ExecutesInOrderAscending()
    {
        // Arrange
        var callOrder = new List<int>();

        var seeder1 = Substitute.For<IDataSeeder>();
        seeder1.Order.Returns(3);
        seeder1.SeedAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(_ => callOrder.Add(3));

        var seeder2 = Substitute.For<IDataSeeder>();
        seeder2.Order.Returns(1);
        seeder2.SeedAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(_ => callOrder.Add(1));

        var seeder3 = Substitute.For<IDataSeeder>();
        seeder3.Order.Returns(2);
        seeder3.SeedAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(_ => callOrder.Add(2));

        var services = new ServiceCollection();
        services.AddSingleton(seeder1);
        services.AddSingleton(seeder2);
        services.AddSingleton(seeder3);
        var sp = services.BuildServiceProvider();

        var logger = Substitute.For<ILogger<DataSeederRunner>>();
        var runner = new DataSeederRunner(sp, logger);

        // Act
        await runner.StartAsync(CancellationToken.None);

        // Assert
        Assert.Equal(new[] { 1, 2, 3 }, callOrder);
    }

    [Fact]
    public async Task StartAsync_SeederThrows_LogsErrorAndContinues()
    {
        // Arrange
        var seeder1 = Substitute.For<IDataSeeder>();
        seeder1.Order.Returns(1);
        seeder1.SeedAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var seeder2 = Substitute.For<IDataSeeder>();
        seeder2.Order.Returns(2);
        seeder2.SeedAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Seeder failed"));

        var seeder3 = Substitute.For<IDataSeeder>();
        seeder3.Order.Returns(3);
        seeder3.SeedAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(seeder1);
        services.AddSingleton(seeder2);
        services.AddSingleton(seeder3);
        var sp = services.BuildServiceProvider();

        var logger = Substitute.For<ILogger<DataSeederRunner>>();
        var runner = new DataSeederRunner(sp, logger);

        // Act
        await runner.StartAsync(CancellationToken.None);

        // Assert — first and third seeders were still called
        await seeder1.Received(1).SeedAsync(Arg.Any<CancellationToken>());
        await seeder3.Received(1).SeedAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_NoSeeders_CompletesWithoutError()
    {
        // Arrange
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();

        var logger = Substitute.For<ILogger<DataSeederRunner>>();
        var runner = new DataSeederRunner(sp, logger);

        // Act & Assert — should not throw
        await runner.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_CompletesImmediately()
    {
        // Arrange
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();

        var logger = Substitute.For<ILogger<DataSeederRunner>>();
        var runner = new DataSeederRunner(sp, logger);

        // Act
        var task = runner.StopAsync(CancellationToken.None);

        // Assert
        Assert.True(task.IsCompleted);
        await task; // should not throw
    }
}

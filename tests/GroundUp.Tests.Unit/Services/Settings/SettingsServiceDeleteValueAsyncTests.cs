using FluentAssertions;
using GroundUp.Core.Entities.Settings;
using GroundUp.Tests.Unit.Services.Settings.TestHelpers;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Unit.Services.Settings;

public sealed class SettingsServiceDeleteValueAsyncTests : IDisposable
{
    private readonly SettingsTestFixture _fixture = new();

    [Fact]
    public async Task DeleteValueAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var service = _fixture.CreateService(context);

        // Act
        var result = await service.DeleteValueAsync(Guid.NewGuid());

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task DeleteValueAsync_Success_RemovesEntityAndReturnsOk()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "DeleteMe",
            allowedLevelIds: levelId);

        var valueId = await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, "to_delete");

        var service = _fixture.CreateService(context);

        // Act
        var result = await service.DeleteValueAsync(valueId);

        // Assert
        result.Success.Should().BeTrue();
        result.StatusCode.Should().Be(200);

        var remaining = await context.Set<SettingValue>()
            .FirstOrDefaultAsync(v => v.Id == valueId);
        remaining.Should().BeNull();
    }

    public void Dispose() => _fixture.Dispose();
}

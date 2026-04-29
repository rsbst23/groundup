using FluentAssertions;
using GroundUp.Core;

namespace GroundUp.Tests.Unit.Core;

/// <summary>
/// Unit tests for <see cref="TenantContext"/>.
/// Validates default state and round-trip of TenantId.
/// </summary>
public sealed class TenantContextTests
{
    [Fact]
    public void TenantContext_DefaultTenantId_IsGuidEmpty()
    {
        // Arrange & Act
        var tenantContext = new TenantContext();

        // Assert
        tenantContext.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void TenantContext_SetTenantId_ReturnsSameValue()
    {
        // Arrange
        var tenantContext = new TenantContext();
        var expected = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");

        // Act
        tenantContext.TenantId = expected;

        // Assert
        tenantContext.TenantId.Should().Be(expected);
    }
}

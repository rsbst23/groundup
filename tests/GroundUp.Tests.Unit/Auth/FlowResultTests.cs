using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Services;

namespace GroundUp.Tests.Unit.Auth;

public sealed class FlowResultTests
{
    [Fact]
    public void Success_TokenIssued_IsNotTenantless()
    {
        var result = FlowResult.Success("token");

        result.IsSuccess.Should().BeTrue();
        result.IsTenantless.Should().BeFalse();
        result.Token.Should().Be("token");
    }

    [Fact]
    public void TenantSelectionRequired_WithTenants_IsTenantlessAndRequiresSelection()
    {
        var tenants = new List<TenantListItemDto> { new(Guid.NewGuid(), "Acme", null) };

        var result = FlowResult.TenantSelectionRequired(tenants);

        result.IsSuccess.Should().BeTrue();
        result.IsTenantless.Should().BeTrue();
        result.RequiresTenantSelection.Should().BeTrue();
        result.TenantList.Should().BeSameAs(tenants);
        result.Token.Should().BeNull();
    }

    [Fact]
    public void NoCurrentTenant_Always_IsTenantlessWithEmptyListAndNoSelection()
    {
        var result = FlowResult.NoCurrentTenant();

        result.IsSuccess.Should().BeTrue();
        result.IsTenantless.Should().BeTrue();
        result.RequiresTenantSelection.Should().BeFalse();
        result.TenantList.Should().NotBeNull().And.BeEmpty();
        result.Token.Should().BeNull();
    }

    [Fact]
    public void Error_Always_IsNotTenantless()
    {
        var result = FlowResult.Error("ACCESS_DENIED", "no", 403);

        result.IsSuccess.Should().BeFalse();
        result.IsTenantless.Should().BeFalse();
    }
}

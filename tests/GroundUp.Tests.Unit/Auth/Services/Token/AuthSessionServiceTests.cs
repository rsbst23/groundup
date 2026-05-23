using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Token;
using GroundUp.Core.Results;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Services.Token;

public sealed class AuthSessionServiceTests
{
    private readonly IUserTenantRepository _userTenantRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly ITokenService _tokenService;
    private readonly AuthSessionService _sut;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _tenantId1 = Guid.NewGuid();
    private readonly Guid _tenantId2 = Guid.NewGuid();

    public AuthSessionServiceTests()
    {
        _userTenantRepository = Substitute.For<IUserTenantRepository>();
        _tenantRepository = Substitute.For<ITenantRepository>();
        _tokenService = Substitute.For<ITokenService>();

        _sut = new AuthSessionService(_userTenantRepository, _tenantRepository, _tokenService);

        // Default token generation returns a token
        _tokenService.GenerateTokenAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IEnumerable<System.Security.Claims.Claim>?>())
            .Returns(callInfo => $"token-for-{callInfo.ArgAt<Guid>(0)}-{callInfo.ArgAt<Guid>(1)}");
    }

    // --- SetTenantAsync: No memberships ---

    [Fact]
    public async Task SetTenantAsync_NoMemberships_ReturnsForbidden()
    {
        // Arrange
        SetupMemberships(new List<UserTenantDto>());

        // Act
        var result = await _sut.SetTenantAsync(_userId, null);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task SetTenantAsync_MembershipQueryFails_ReturnsForbidden()
    {
        // Arrange
        _userTenantRepository.GetAllMembershipsForUserAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserTenantDto>>.Fail("DB error", 500));

        // Act
        var result = await _sut.SetTenantAsync(_userId, null);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
    }

    // --- SetTenantAsync: Single tenant auto-select ---

    [Fact]
    public async Task SetTenantAsync_SingleTenant_NullTenantId_AutoSelectsAndReturnsToken()
    {
        // Arrange
        SetupMemberships(new List<UserTenantDto>
        {
            new(Guid.NewGuid(), _userId, _tenantId1, "ext-1", true)
        });

        // Act
        var result = await _sut.SetTenantAsync(_userId, null);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.Data!.SelectionRequired);
        Assert.Null(result.Data.AvailableTenants);
        Assert.NotNull(result.Data.Token);
    }

    [Fact]
    public async Task SetTenantAsync_SingleTenant_AutoSelect_CallsTokenServiceWithCorrectTenant()
    {
        // Arrange
        SetupMemberships(new List<UserTenantDto>
        {
            new(Guid.NewGuid(), _userId, _tenantId1, "ext-1", true)
        });

        // Act
        await _sut.SetTenantAsync(_userId, null);

        // Assert
        await _tokenService.Received(1).GenerateTokenAsync(_userId, _tenantId1, Arg.Any<IEnumerable<System.Security.Claims.Claim>?>());
    }

    [Fact]
    public async Task SetTenantAsync_SingleTenant_TokenGenerationFails_ReturnsNotFound()
    {
        // Arrange
        SetupMemberships(new List<UserTenantDto>
        {
            new(Guid.NewGuid(), _userId, _tenantId1, "ext-1", true)
        });
        _tokenService.GenerateTokenAsync(_userId, _tenantId1, Arg.Any<IEnumerable<System.Security.Claims.Claim>?>())
            .Returns((string?)null);

        // Act
        var result = await _sut.SetTenantAsync(_userId, null);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
    }

    // --- SetTenantAsync: Multiple tenants ---

    [Fact]
    public async Task SetTenantAsync_MultipleTenants_NullTenantId_ReturnsSelectionRequired()
    {
        // Arrange
        SetupMemberships(new List<UserTenantDto>
        {
            new(Guid.NewGuid(), _userId, _tenantId1, "ext-1", true),
            new(Guid.NewGuid(), _userId, _tenantId2, "ext-2", true)
        });
        SetupTenants(
            new TenantDto(_tenantId1, "Tenant One", "tenant-one", TenantType.Standard, OnboardingMode.InviteOnly, null, null, true),
            new TenantDto(_tenantId2, "Tenant Two", "tenant-two", TenantType.Standard, OnboardingMode.InviteOnly, null, null, true)
        );

        // Act
        var result = await _sut.SetTenantAsync(_userId, null);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.Data!.SelectionRequired);
        Assert.Null(result.Data.Token);
        Assert.NotNull(result.Data.AvailableTenants);
        Assert.Equal(2, result.Data.AvailableTenants.Count);
    }

    [Fact]
    public async Task SetTenantAsync_MultipleTenants_ReturnsTenantNames()
    {
        // Arrange
        SetupMemberships(new List<UserTenantDto>
        {
            new(Guid.NewGuid(), _userId, _tenantId1, "ext-1", true),
            new(Guid.NewGuid(), _userId, _tenantId2, "ext-2", true)
        });
        SetupTenants(
            new TenantDto(_tenantId1, "Tenant One", "tenant-one", TenantType.Standard, OnboardingMode.InviteOnly, null, null, true),
            new TenantDto(_tenantId2, "Tenant Two", "tenant-two", TenantType.Standard, OnboardingMode.InviteOnly, null, null, true)
        );

        // Act
        var result = await _sut.SetTenantAsync(_userId, null);

        // Assert
        var tenantNames = result.Data!.AvailableTenants!.Select(t => t.Name).ToList();
        Assert.Contains("Tenant One", tenantNames);
        Assert.Contains("Tenant Two", tenantNames);
    }

    // --- SetTenantAsync: Explicit tenant selection ---

    [Fact]
    public async Task SetTenantAsync_ExplicitTenantId_UserIsMember_ReturnsToken()
    {
        // Arrange
        SetupMemberships(new List<UserTenantDto>
        {
            new(Guid.NewGuid(), _userId, _tenantId1, "ext-1", true),
            new(Guid.NewGuid(), _userId, _tenantId2, "ext-2", true)
        });

        // Act
        var result = await _sut.SetTenantAsync(_userId, _tenantId1);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.Data!.SelectionRequired);
        Assert.NotNull(result.Data.Token);
    }

    [Fact]
    public async Task SetTenantAsync_ExplicitTenantId_UserNotMember_ReturnsForbidden()
    {
        // Arrange
        SetupMemberships(new List<UserTenantDto>
        {
            new(Guid.NewGuid(), _userId, _tenantId1, "ext-1", true)
        });
        var nonMemberTenantId = Guid.NewGuid();

        // Act
        var result = await _sut.SetTenantAsync(_userId, nonMemberTenantId);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
        Assert.Contains("does not belong", result.Message);
    }

    // --- RefreshTokenAsync ---

    [Fact]
    public async Task RefreshTokenAsync_UserStillMember_ReturnsNewToken()
    {
        // Arrange
        SetupMemberships(new List<UserTenantDto>
        {
            new(Guid.NewGuid(), _userId, _tenantId1, "ext-1", true)
        });

        // Act
        var result = await _sut.RefreshTokenAsync(_userId, _tenantId1);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task RefreshTokenAsync_UserNoLongerMember_ReturnsForbidden()
    {
        // Arrange — user has memberships but not for the requested tenant
        SetupMemberships(new List<UserTenantDto>
        {
            new(Guid.NewGuid(), _userId, _tenantId2, "ext-2", true)
        });

        // Act
        var result = await _sut.RefreshTokenAsync(_userId, _tenantId1);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task RefreshTokenAsync_MembershipQueryFails_ReturnsForbidden()
    {
        // Arrange
        _userTenantRepository.GetAllMembershipsForUserAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserTenantDto>>.Fail("DB error", 500));

        // Act
        var result = await _sut.RefreshTokenAsync(_userId, _tenantId1);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task RefreshTokenAsync_TokenGenerationFails_ReturnsNotFound()
    {
        // Arrange
        SetupMemberships(new List<UserTenantDto>
        {
            new(Guid.NewGuid(), _userId, _tenantId1, "ext-1", true)
        });
        _tokenService.GenerateTokenAsync(_userId, _tenantId1, Arg.Any<IEnumerable<System.Security.Claims.Claim>?>())
            .Returns((string?)null);

        // Act
        var result = await _sut.RefreshTokenAsync(_userId, _tenantId1);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task RefreshTokenAsync_ReValidatesMembership_BeforeGeneratingToken()
    {
        // Arrange
        SetupMemberships(new List<UserTenantDto>
        {
            new(Guid.NewGuid(), _userId, _tenantId1, "ext-1", true)
        });

        // Act
        await _sut.RefreshTokenAsync(_userId, _tenantId1);

        // Assert — membership was queried
        await _userTenantRepository.Received(1).GetAllMembershipsForUserAsync(_userId, Arg.Any<CancellationToken>());
        // And token was generated for the correct tenant
        await _tokenService.Received(1).GenerateTokenAsync(_userId, _tenantId1, Arg.Any<IEnumerable<System.Security.Claims.Claim>?>());
    }

    // --- Inactive membership tests (security-critical) ---

    [Fact]
    public async Task SetTenantAsync_OnlyInactiveMembership_NullTenantId_ReturnsForbidden()
    {
        // Arrange — user has one membership, but it's inactive
        SetupMemberships(new List<UserTenantDto>
        {
            new(Guid.NewGuid(), _userId, _tenantId1, "ext-1", IsActive: false)
        });

        // Act
        var result = await _sut.SetTenantAsync(_userId, null);

        // Assert — inactive memberships are filtered out, leaving zero → Forbidden
        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task SetTenantAsync_ExplicitTenantId_InactiveMembership_ReturnsForbidden()
    {
        // Arrange — user has only an inactive membership for the requested tenant
        SetupMemberships(new List<UserTenantDto>
        {
            new(Guid.NewGuid(), _userId, _tenantId1, "ext-1", IsActive: false),
            new(Guid.NewGuid(), _userId, _tenantId2, "ext-2", IsActive: true)
        });

        // Act — request the inactive tenant
        var result = await _sut.SetTenantAsync(_userId, _tenantId1);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task SetTenantAsync_MixedActiveAndInactive_NullTenantId_AutoSelectsOnlyActive()
    {
        // Arrange — one inactive, one active membership → only the active one counts as "single"
        SetupMemberships(new List<UserTenantDto>
        {
            new(Guid.NewGuid(), _userId, _tenantId1, "ext-1", IsActive: false),
            new(Guid.NewGuid(), _userId, _tenantId2, "ext-2", IsActive: true)
        });

        // Act
        var result = await _sut.SetTenantAsync(_userId, null);

        // Assert — auto-selects the active tenant, no selection list
        Assert.True(result.Success);
        Assert.False(result.Data!.SelectionRequired);
        await _tokenService.Received(1).GenerateTokenAsync(_userId, _tenantId2, Arg.Any<IEnumerable<System.Security.Claims.Claim>?>());
    }

    [Fact]
    public async Task RefreshTokenAsync_InactiveMembership_ReturnsForbidden()
    {
        // Arrange — membership exists but is inactive
        SetupMemberships(new List<UserTenantDto>
        {
            new(Guid.NewGuid(), _userId, _tenantId1, "ext-1", IsActive: false)
        });

        // Act
        var result = await _sut.RefreshTokenAsync(_userId, _tenantId1);

        // Assert — inactive memberships cannot refresh
        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
    }

    // --- Tenant lookup failure ---

    [Fact]
    public async Task SetTenantAsync_MultipleTenants_TenantLookupFails_ReturnsError()
    {
        // Arrange — multi-tenant case, but the tenant repository fails to load details
        SetupMemberships(new List<UserTenantDto>
        {
            new(Guid.NewGuid(), _userId, _tenantId1, "ext-1", true),
            new(Guid.NewGuid(), _userId, _tenantId2, "ext-2", true)
        });
        _tenantRepository.GetByIdsBypassFilterAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<TenantDto>>.Fail("DB connection lost", 500));

        // Act
        var result = await _sut.SetTenantAsync(_userId, null);

        // Assert — surfaces the error rather than returning an empty selection list
        Assert.False(result.Success);
        Assert.Equal(500, result.StatusCode);
    }

    [Fact]
    public async Task SetTenantAsync_MultipleTenants_OneTenantMissing_OnlyReturnsFound()
    {
        // Arrange — user has 2 memberships, but tenant repo only returns 1 (other was hard-deleted)
        SetupMemberships(new List<UserTenantDto>
        {
            new(Guid.NewGuid(), _userId, _tenantId1, "ext-1", true),
            new(Guid.NewGuid(), _userId, _tenantId2, "ext-2", true)
        });
        // Only one tenant resolves
        _tenantRepository.GetByIdsBypassFilterAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<TenantDto>>.Ok(new List<TenantDto>
            {
                new(_tenantId1, "Tenant One", "tenant-one", TenantType.Standard, OnboardingMode.InviteOnly, null, null, true)
            }));

        // Act
        var result = await _sut.SetTenantAsync(_userId, null);

        // Assert — returns the one we found
        Assert.True(result.Success);
        Assert.True(result.Data!.SelectionRequired);
        Assert.Single(result.Data.AvailableTenants!);
        Assert.Equal(_tenantId1, result.Data.AvailableTenants![0].Id);
    }

    // --- Helper methods ---

    private void SetupMemberships(List<UserTenantDto> memberships)
    {
        _userTenantRepository.GetAllMembershipsForUserAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserTenantDto>>.Ok(memberships));
    }

    private void SetupTenants(params TenantDto[] tenants)
    {
        _tenantRepository.GetByIdsBypassFilterAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<TenantDto>>.Ok(tenants.ToList()));
    }
}

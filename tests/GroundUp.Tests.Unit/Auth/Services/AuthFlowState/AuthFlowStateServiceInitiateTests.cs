using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Core.Results;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Services.AuthFlowState;

public sealed class AuthFlowStateServiceInitiateTests
{
    private readonly IAuthFlowStateRepository _repository;
    private readonly IValidator<InitiateAuthFlowRequest> _validator;
    private readonly AuthFlowStateService _sut;

    public AuthFlowStateServiceInitiateTests()
    {
        _repository = Substitute.For<IAuthFlowStateRepository>();
        _validator = Substitute.For<IValidator<InitiateAuthFlowRequest>>();
        _sut = new AuthFlowStateService(_repository, _validator);

        // Default: validation passes
        _validator.ValidateAsync(Arg.Any<InitiateAuthFlowRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());

        // Default: repo returns whatever is passed in with a generated Id
        _repository.AddAsync(Arg.Any<AuthFlowStateDto>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var dto = ci.Arg<AuthFlowStateDto>();
                var created = dto with { Id = Guid.NewGuid() };
                return OperationResult<AuthFlowStateDto>.Ok(created);
            });
    }

    [Fact]
    public async Task InitiateAsync_NullLifetime_AppliesDefaultFifteenMinutes()
    {
        // Arrange
        var request = CreateValidRequest(lifetime: null);
        var before = DateTime.UtcNow;

        // Act
        var result = await _sut.InitiateAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        var expectedExpiry = before.AddMinutes(15);
        result.Data!.ExpiresAt.Should().BeCloseTo(expectedExpiry, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task InitiateAsync_CustomLifetime_AppliesCorrectExpiry()
    {
        // Arrange
        var customLifetime = TimeSpan.FromMinutes(30);
        var request = CreateValidRequest(lifetime: customLifetime);
        var before = DateTime.UtcNow;

        // Act
        var result = await _sut.InitiateAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        var expectedExpiry = before.Add(customLifetime);
        result.Data!.ExpiresAt.Should().BeCloseTo(expectedExpiry, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task InitiateAsync_EmptyNonce_ReturnsBadRequest()
    {
        // Arrange
        var request = CreateValidRequest();
        _validator.ValidateAsync(Arg.Any<InitiateAuthFlowRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult(new[]
            {
                new ValidationFailure("Nonce", "'Nonce' must not be empty.")
            }));

        // Act
        var result = await _sut.InitiateAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("Nonce");
    }

    [Fact]
    public async Task InitiateAsync_NonceExceeds128Chars_ReturnsBadRequest()
    {
        // Arrange
        var request = CreateValidRequest();
        _validator.ValidateAsync(Arg.Any<InitiateAuthFlowRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult(new[]
            {
                new ValidationFailure("Nonce", "The length of 'Nonce' must be 128 characters or fewer.")
            }));

        // Act
        var result = await _sut.InitiateAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("Nonce");
    }

    private static InitiateAuthFlowRequest CreateValidRequest(TimeSpan? lifetime = null) =>
        new(
            FlowType: FlowType.NewOrganization,
            TenantId: null,
            InvitationId: null,
            JoinLinkId: null,
            Realm: null,
            ReturnUrl: null,
            StateToken: "valid-state-token",
            CodeVerifier: "valid-code-verifier-with-enough-chars-1234567",
            RedirectUri: "https://example.com/auth/callback",
            OrganizationName: null,
            Nonce: "valid-nonce-value",
            CreatedByIp: "127.0.0.1",
            CreatedByUserAgent: "TestAgent/1.0",
            Lifetime: lifetime);
}

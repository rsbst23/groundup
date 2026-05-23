using FluentAssertions;
using FluentValidation.TestHelper;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Core.Validators;

namespace GroundUp.Tests.Unit.Auth.Validators;

public sealed class InitiateAuthFlowRequestValidatorTests
{
    private readonly InitiateAuthFlowRequestValidator _sut = new();

    [Fact]
    public async Task Validate_EmptyNonce_Fails()
    {
        // Arrange
        var request = CreateValidRequest() with { Nonce = "" };

        // Act
        var result = await _sut.TestValidateAsync(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Nonce);
    }

    [Fact]
    public async Task Validate_NonceExceeds128Chars_Fails()
    {
        // Arrange
        var request = CreateValidRequest() with { Nonce = new string('a', 129) };

        // Act
        var result = await _sut.TestValidateAsync(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Nonce);
    }

    [Fact]
    public async Task Validate_InvalidFlowType_Fails()
    {
        // Arrange
        var request = CreateValidRequest() with { FlowType = (FlowType)999 };

        // Act
        var result = await _sut.TestValidateAsync(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.FlowType);
    }

    [Fact]
    public async Task Validate_RealmExceeds128Chars_Fails()
    {
        // Arrange
        var request = CreateValidRequest() with { Realm = new string('r', 129) };

        // Act
        var result = await _sut.TestValidateAsync(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Realm);
    }

    [Fact]
    public async Task Validate_ReturnUrlExceeds2048Chars_Fails()
    {
        // Arrange
        var request = CreateValidRequest() with { ReturnUrl = new string('u', 2049) };

        // Act
        var result = await _sut.TestValidateAsync(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.ReturnUrl);
    }

    [Fact]
    public async Task Validate_CreatedByIpExceeds64Chars_Fails()
    {
        // Arrange
        var request = CreateValidRequest() with { CreatedByIp = new string('i', 65) };

        // Act
        var result = await _sut.TestValidateAsync(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.CreatedByIp);
    }

    [Fact]
    public async Task Validate_CreatedByUserAgentExceeds512Chars_Fails()
    {
        // Arrange
        var request = CreateValidRequest() with { CreatedByUserAgent = new string('u', 513) };

        // Act
        var result = await _sut.TestValidateAsync(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.CreatedByUserAgent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-60)]
    public async Task Validate_LifetimeZeroOrNegative_Fails(int seconds)
    {
        // Arrange
        var request = CreateValidRequest() with { Lifetime = TimeSpan.FromSeconds(seconds) };

        // Act
        var result = await _sut.TestValidateAsync(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Lifetime);
    }

    [Fact]
    public async Task Validate_ValidRequest_PassesAllRules()
    {
        // Arrange
        var request = CreateValidRequest();

        // Act
        var result = await _sut.TestValidateAsync(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task Validate_NullLifetime_Passes()
    {
        // Arrange
        var request = CreateValidRequest() with { Lifetime = null };

        // Act
        var result = await _sut.TestValidateAsync(request);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Lifetime);
    }

    private static InitiateAuthFlowRequest CreateValidRequest() =>
        new(
            FlowType: FlowType.NewOrganization,
            TenantId: Guid.NewGuid(),
            InvitationId: null,
            JoinLinkId: null,
            Realm: "my-realm",
            ReturnUrl: "https://example.com/callback",
            Nonce: "valid-nonce-value",
            CreatedByIp: "127.0.0.1",
            CreatedByUserAgent: "TestAgent/1.0",
            Lifetime: TimeSpan.FromMinutes(10));
}

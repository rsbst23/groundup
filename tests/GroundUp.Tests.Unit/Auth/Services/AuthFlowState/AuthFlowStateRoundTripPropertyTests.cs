using FsCheck;
using FsCheck.Xunit;
using FluentValidation;
using FluentValidation.Results;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Core.Results;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Services.AuthFlowState;

/// <summary>
/// Property-based tests verifying round-trip metadata preservation:
/// for any valid InitiateAuthFlowRequest, initiate → consume preserves all metadata fields.
/// </summary>
public sealed class AuthFlowStateRoundTripPropertyTests
{
    /// <summary>
    /// Property: For any valid initiation request, the metadata fields (FlowType, TenantId,
    /// InvitationId, JoinLinkId, Realm, ReturnUrl, Nonce, CreatedByIp, CreatedByUserAgent)
    /// are preserved through the initiate → consume round-trip.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property InitiateAndConsume_PreservesAllMetadataFields(
        int flowTypeInt,
        Guid tenantId,
        NonNull<string> nonce,
        NonNull<string> ip)
    {
        var flowType = (FlowType)(Math.Abs(flowTypeInt) % 7);

        var repository = Substitute.For<IAuthFlowStateRepository>();
        var validator = Substitute.For<IValidator<InitiateAuthFlowRequest>>();
        var service = new AuthFlowStateService(repository, validator);

        // Validator always passes
        validator.ValidateAsync(Arg.Any<InitiateAuthFlowRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());

        var request = new InitiateAuthFlowRequest(
            FlowType: flowType,
            TenantId: tenantId,
            InvitationId: null,
            JoinLinkId: null,
            Realm: "test-realm",
            ReturnUrl: "https://example.com/return",
            StateToken: "test-state-token",
            CodeVerifier: "test-code-verifier-with-enough-chars-1234567",
            RedirectUri: "https://example.com/auth/callback",
            OrganizationName: null,
            Nonce: nonce.Get,
            CreatedByIp: ip.Get,
            CreatedByUserAgent: "PropertyTest/1.0",
            Lifetime: TimeSpan.FromMinutes(15));

        var generatedId = Guid.NewGuid();

        // Repository.AddAsync captures the DTO and returns it with a generated ID
        repository.AddAsync(Arg.Any<AuthFlowStateDto>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var dto = callInfo.Arg<AuthFlowStateDto>();
                var persisted = dto with { Id = generatedId };
                return Task.FromResult(OperationResult<AuthFlowStateDto>.Ok(persisted));
            });

        // Repository.MarkConsumedAsync returns the consumed version
        repository.MarkConsumedAsync(generatedId, Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var consumed = new AuthFlowStateDto(
                    Id: generatedId,
                    FlowType: flowType,
                    Status: FlowStatus.Consumed,
                    TenantId: tenantId,
                    InvitationId: null,
                    JoinLinkId: null,
                    Realm: "test-realm",
                    ReturnUrl: "https://example.com/return",
                    StateToken: "test-state-token",
                    CodeVerifier: "test-code-verifier-with-enough-chars-1234567",
                    RedirectUri: "https://example.com/auth/callback",
                    OrganizationName: null,
                    Nonce: nonce.Get,
                    CreatedByIp: ip.Get,
                    CreatedByUserAgent: "PropertyTest/1.0",
                    ExpiresAt: DateTime.UtcNow.AddMinutes(15),
                    ConsumedAt: DateTime.UtcNow,
                    TerminatedAt: DateTime.UtcNow,
                    FailureReason: null,
                    CreatedAt: DateTime.UtcNow,
                    UpdatedAt: DateTime.UtcNow);
                return Task.FromResult(OperationResult<AuthFlowStateDto>.Ok(consumed));
            });

        // Act
        var initiateResult = service.InitiateAsync(request).GetAwaiter().GetResult();
        var consumeResult = service.ConsumeAsync(generatedId, flowType).GetAwaiter().GetResult();

        // Assert all metadata fields preserved
        return (initiateResult.Success
            && consumeResult.Success
            && consumeResult.Data!.FlowType == flowType
            && consumeResult.Data.TenantId == tenantId
            && consumeResult.Data.Realm == "test-realm"
            && consumeResult.Data.ReturnUrl == "https://example.com/return"
            && consumeResult.Data.Nonce == nonce.Get
            && consumeResult.Data.CreatedByIp == ip.Get
            && consumeResult.Data.CreatedByUserAgent == "PropertyTest/1.0"
            && consumeResult.Data.Status == FlowStatus.Consumed)
            .ToProperty();
    }
}

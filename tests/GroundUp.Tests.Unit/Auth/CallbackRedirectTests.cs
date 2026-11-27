using FluentAssertions;
using GroundUp.Auth.Api;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Services;

namespace GroundUp.Tests.Unit.Auth;

/// <summary>
/// GU-87: the address the callback redirects to is the return URL with the outcome in its query
/// string, and nothing secret.
/// </summary>
public sealed class CallbackRedirectTests
{
    [Fact]
    public void Build_SignedIn_AddsResultSignedIn()
    {
        CallbackRedirect.Build("/session", FlowResult.Success("secret-token")).Should().Be("/session?result=signed_in");
    }

    [Fact]
    public void Build_AlreadyMember_AddsAlreadyMember()
    {
        CallbackRedirect.Build("/session", FlowResult.Success("t", alreadyMember: true))
            .Should().Be("/session?result=signed_in&alreadyMember=true");
    }

    [Fact]
    public void Build_TenantPicker_AddsTenantPicker()
    {
        var picker = FlowResult.TenantSelectionRequired([new TenantListItemDto(Guid.NewGuid(), "Acme", null)]);

        CallbackRedirect.Build("/session", picker).Should().Be("/session?result=tenant_picker");
    }

    [Fact]
    public void Build_NoTenant_AddsNoTenant()
    {
        CallbackRedirect.Build("/session", FlowResult.NoCurrentTenant()).Should().Be("/session?result=no_tenant");
    }

    [Fact]
    public void Build_Error_AddsTheCodeOnly()
    {
        var error = FlowResult.Error("INVITATION_EMAIL_MISMATCH", "This invitation was sent to a@example.com.", 403);

        var url = CallbackRedirect.Build("/invite/abc", error);

        url.Should().Be("/invite/abc?error=INVITATION_EMAIL_MISMATCH");
        url.Should().NotContain("example.com");
    }

    [Fact]
    public void Build_ProviderError_AddsIdpError()
    {
        var error = FlowResult.Error("IDP_ERROR", "The identity provider reported an error.") with { IdpError = "access_denied" };

        CallbackRedirect.Build("/session", error).Should().Be("/session?error=IDP_ERROR&idpError=access_denied");
    }

    [Fact]
    public void Build_ExistingQuery_KeptWithTheOutcomeLast()
    {
        CallbackRedirect.Build("/invite/abc?org=acme&tab=2", FlowResult.Success("t"))
            .Should().Be("/invite/abc?org=acme&tab=2&result=signed_in");
    }

    [Fact]
    public void Build_ExistingOutcomeValues_Replaced()
    {
        CallbackRedirect.Build("/session?result=signed_in&error=OLD&alreadyMember=true&idpError=x&keep=1", FlowResult.NoCurrentTenant())
            .Should().Be("/session?keep=1&result=no_tenant");
    }

    [Fact]
    public void Build_ValuesAreEncoded()
    {
        CallbackRedirect.Build("/a?q=x%20y", FlowResult.Success("t")).Should().Be("/a?q=x%20y&result=signed_in");
    }

    [Fact]
    public void Build_NeverIncludesTheToken()
    {
        CallbackRedirect.Build("/session", FlowResult.Success("eyJ-secret-token")).Should().NotContain("eyJ");
    }
}

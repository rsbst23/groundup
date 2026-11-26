using FluentAssertions;
using GroundUp.Auth.Services.Token;

namespace GroundUp.Tests.Unit.Auth.Invitations;

/// <summary>
/// GU-68 Requirement 6.4: invitation and join-link tokens.
/// </summary>
public sealed class OpaqueTokenTests
{
    [Fact]
    public void Create_Is43UrlSafeCharactersOf256Bits()
    {
        var token = OpaqueToken.Create();

        token.Should().HaveLength(43, "32 bytes in Base64Url without padding");
        token.Should().MatchRegex("^[A-Za-z0-9_-]+$");
    }

    [Fact]
    public void Create_IsDifferentEveryTime()
    {
        var tokens = Enumerable.Range(0, 1000).Select(_ => OpaqueToken.Create()).ToList();

        tokens.Distinct().Should().HaveCount(1000);
    }

    [Fact]
    public void Hash_IsLowerCaseHexSha256_AndStable()
    {
        var token = OpaqueToken.Create();

        var hash = OpaqueToken.Hash(token);

        hash.Should().MatchRegex("^[0-9a-f]{64}$");
        OpaqueToken.Hash(token).Should().Be(hash);
        OpaqueToken.Hash(token + "x").Should().NotBe(hash);
    }

    [Fact]
    public void Hash_KnownValue()
        => OpaqueToken.Hash("abc").Should().Be("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
}

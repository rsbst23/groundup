using System.Security.Cryptography;
using System.Text;

namespace GroundUp.Auth.Services.Token;

/// <summary>
/// Random bearer tokens for invitations and join links (GU-68): 256 random bits, Base64Url without
/// padding (43 characters). Only the SHA-256 hash is stored; a plain hash is enough because the token
/// has 256 bits of entropy, so a slow hash would add nothing.
/// </summary>
internal static class OpaqueToken
{
    private const int ByteLength = 32;

    /// <summary>Creates a new token.</summary>
    public static string Create()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(ByteLength))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    /// <summary>The lower-case hex SHA-256 of <paramref name="token"/>, as stored.</summary>
    public static string Hash(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}

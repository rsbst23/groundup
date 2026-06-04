using FluentAssertions;
using GroundUp.Services.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GroundUp.Tests.Unit.Services.Security;

/// <summary>
/// Unit tests for <see cref="EnvironmentFileMasterKeyProvider"/>.
/// Validates master key resolution from file path and environment variable sources.
/// </summary>
public sealed class EnvironmentFileMasterKeyProviderTests : IDisposable
{
    private readonly IConfiguration _configuration = Substitute.For<IConfiguration>();
    private readonly ILogger<EnvironmentFileMasterKeyProvider> _logger =
        Substitute.For<ILogger<EnvironmentFileMasterKeyProvider>>();

    private readonly List<string> _tempFiles = new();

    /// <summary>
    /// Creates a temporary file with the given content and returns the path.
    /// </summary>
    private string CreateTempKeyFile(string content)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    /// <summary>
    /// Generates a valid 32-byte key encoded as base64.
    /// </summary>
    private static string GenerateValidBase64Key()
    {
        var key = new byte[32];
        for (var i = 0; i < 32; i++) key[i] = (byte)(i + 1);
        return Convert.ToBase64String(key);
    }

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    #region File Path Tests

    [Fact]
    public void GetKey_FilePathConfigured_FileNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".key");
        _configuration["GroundUp:MasterKeyPath"].Returns(nonExistentPath);
        _configuration["GroundUp:MasterKey"].Returns((string?)null);

        var provider = new EnvironmentFileMasterKeyProvider(_configuration, _logger);

        // Act
        var act = () => provider.GetKey();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*'{nonExistentPath}'*");
    }

    [Fact]
    public void GetKey_FilePathConfigured_FileEmpty_ThrowsInvalidOperationException()
    {
        // Arrange
        var path = CreateTempKeyFile("   \n  ");
        _configuration["GroundUp:MasterKeyPath"].Returns(path);
        _configuration["GroundUp:MasterKey"].Returns((string?)null);

        var provider = new EnvironmentFileMasterKeyProvider(_configuration, _logger);

        // Act
        var act = () => provider.GetKey();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*empty*");
    }

    [Fact]
    public void GetKey_FilePathConfigured_InvalidBase64_ThrowsInvalidOperationException()
    {
        // Arrange
        var path = CreateTempKeyFile("not-valid-base64!!!");
        _configuration["GroundUp:MasterKeyPath"].Returns(path);
        _configuration["GroundUp:MasterKey"].Returns((string?)null);

        var provider = new EnvironmentFileMasterKeyProvider(_configuration, _logger);

        // Act
        var act = () => provider.GetKey();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not valid base64*");
    }

    [Fact]
    public void GetKey_FilePathConfigured_KeyTooShort_ThrowsInvalidOperationException()
    {
        // Arrange — 16 bytes is too short (need 32)
        var shortKey = Convert.ToBase64String(new byte[16]);
        var path = CreateTempKeyFile(shortKey);
        _configuration["GroundUp:MasterKeyPath"].Returns(path);
        _configuration["GroundUp:MasterKey"].Returns((string?)null);

        var provider = new EnvironmentFileMasterKeyProvider(_configuration, _logger);

        // Act
        var act = () => provider.GetKey();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*16 bytes*");
    }

    [Fact]
    public void GetKey_FilePathConfigured_ValidKey_ReturnsDecodedBytes()
    {
        // Arrange
        var base64Key = GenerateValidBase64Key();
        var expectedBytes = Convert.FromBase64String(base64Key);
        var path = CreateTempKeyFile(base64Key);
        _configuration["GroundUp:MasterKeyPath"].Returns(path);
        _configuration["GroundUp:MasterKey"].Returns((string?)null);

        var provider = new EnvironmentFileMasterKeyProvider(_configuration, _logger);

        // Act
        var result = provider.GetKey();

        // Assert
        result.Should().BeEquivalentTo(expectedBytes);
    }

    [Fact]
    public void GetKey_FilePathConfigured_WhitespaceAroundKey_TrimsAndDecodes()
    {
        // Arrange
        var base64Key = GenerateValidBase64Key();
        var expectedBytes = Convert.FromBase64String(base64Key);
        var contentWithWhitespace = $"  \n  {base64Key}  \r\n  ";
        var path = CreateTempKeyFile(contentWithWhitespace);
        _configuration["GroundUp:MasterKeyPath"].Returns(path);
        _configuration["GroundUp:MasterKey"].Returns((string?)null);

        var provider = new EnvironmentFileMasterKeyProvider(_configuration, _logger);

        // Act
        var result = provider.GetKey();

        // Assert
        result.Should().BeEquivalentTo(expectedBytes);
    }

    #endregion

    #region Environment Variable Tests

    [Fact]
    public void GetKey_EnvVarConfigured_ValidKey_ReturnsDecodedBytes()
    {
        // Arrange
        var base64Key = GenerateValidBase64Key();
        var expectedBytes = Convert.FromBase64String(base64Key);
        _configuration["GroundUp:MasterKeyPath"].Returns((string?)null);
        _configuration["GroundUp:MasterKey"].Returns(base64Key);

        var provider = new EnvironmentFileMasterKeyProvider(_configuration, _logger);

        // Act
        var result = provider.GetKey();

        // Assert
        result.Should().BeEquivalentTo(expectedBytes);
    }

    #endregion

    #region Neither Configured Tests

    [Fact]
    public void GetKey_NeitherConfigured_ThrowsInvalidOperationException()
    {
        // Arrange
        _configuration["GroundUp:MasterKeyPath"].Returns((string?)null);
        _configuration["GroundUp:MasterKey"].Returns((string?)null);

        var provider = new EnvironmentFileMasterKeyProvider(_configuration, _logger);

        // Act
        var act = () => provider.GetKey();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*GroundUp:MasterKeyPath*")
            .WithMessage("*GroundUp:MasterKey*");
    }

    #endregion

    #region Both Configured Tests

    [Fact]
    public void GetKey_BothConfigured_PrefersFileAndLogsWarning()
    {
        // Arrange
        var fileBase64Key = GenerateValidBase64Key();
        var envBase64Key = Convert.ToBase64String(new byte[32]); // Different key
        var expectedBytes = Convert.FromBase64String(fileBase64Key);
        var path = CreateTempKeyFile(fileBase64Key);

        _configuration["GroundUp:MasterKeyPath"].Returns(path);
        _configuration["GroundUp:MasterKey"].Returns(envBase64Key);

        var provider = new EnvironmentFileMasterKeyProvider(_configuration, _logger);

        // Act
        var result = provider.GetKey();

        // Assert — file key is used
        result.Should().BeEquivalentTo(expectedBytes);

        // Assert — warning was logged
        _logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Both")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    #endregion

    #region Caching Tests

    [Fact]
    public void GetKey_CachesAfterFirstCall_DoesNotRereadFile()
    {
        // Arrange
        var base64Key = GenerateValidBase64Key();
        var path = CreateTempKeyFile(base64Key);
        _configuration["GroundUp:MasterKeyPath"].Returns(path);
        _configuration["GroundUp:MasterKey"].Returns((string?)null);

        var provider = new EnvironmentFileMasterKeyProvider(_configuration, _logger);

        // Act — call twice
        var result1 = provider.GetKey();
        var result2 = provider.GetKey();

        // Assert — same reference returned (cached)
        result1.Should().BeSameAs(result2);

        // Verify configuration was only accessed during the first call
        // (The provider caches after first resolution)
        _ = _configuration.Received(1)["GroundUp:MasterKeyPath"];
    }

    #endregion
}

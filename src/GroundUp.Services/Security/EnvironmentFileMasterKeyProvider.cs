using GroundUp.Core.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GroundUp.Services.Security;

/// <summary>
/// Resolves the 256-bit master key from a file path (<c>GroundUp:MasterKeyPath</c>) or
/// a base64-encoded environment variable (<c>GroundUp:MasterKey</c>) with file-first priority.
/// </summary>
/// <remarks>
/// <para>
/// Resolution algorithm:
/// <list type="number">
///   <item>Read <c>GroundUp:MasterKeyPath</c> from configuration.</item>
///   <item>If set AND <c>GroundUp:MasterKey</c> also set → log warning, proceed with file.</item>
///   <item>If file path set → read file, trim whitespace, base64-decode, validate length ≥ 32 bytes.</item>
///   <item>Else read <c>GroundUp:MasterKey</c> → trim, base64-decode, validate length ≥ 32 bytes.</item>
///   <item>Neither configured → throw naming both config keys.</item>
/// </list>
/// </para>
/// <para>
/// On Unix systems, if the key file has group or other read/write/execute permissions,
/// a warning is logged recommending file mode 0600. Startup is NOT blocked.
/// </para>
/// <para>
/// This class is sealed and singleton-safe. The resolved key is cached after first
/// resolution using double-checked locking; subsequent calls return the same byte array
/// without re-reading the source.
/// </para>
/// </remarks>
public sealed class EnvironmentFileMasterKeyProvider : IMasterKeyProvider
{
    private const string MasterKeyPathConfigKey = "GroundUp:MasterKeyPath";
    private const string MasterKeyConfigKey = "GroundUp:MasterKey";

    private readonly IConfiguration _configuration;
    private readonly ILogger<EnvironmentFileMasterKeyProvider> _logger;
    private byte[]? _cachedKey;
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="EnvironmentFileMasterKeyProvider"/> class.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="logger">The logger instance.</param>
    public EnvironmentFileMasterKeyProvider(
        IConfiguration configuration,
        ILogger<EnvironmentFileMasterKeyProvider> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public byte[] GetKey()
    {
        if (_cachedKey is not null)
            return _cachedKey;

        lock (_lock)
        {
            if (_cachedKey is not null)
                return _cachedKey;

            _cachedKey = ResolveKey();
            return _cachedKey;
        }
    }

    private byte[] ResolveKey()
    {
        var filePath = _configuration[MasterKeyPathConfigKey]?.Trim();
        var masterKeyValue = _configuration[MasterKeyConfigKey]?.Trim();

        var hasFilePath = !string.IsNullOrEmpty(filePath);
        var hasMasterKey = !string.IsNullOrEmpty(masterKeyValue);

        if (hasFilePath && hasMasterKey)
        {
            _logger.LogWarning(
                "Both GroundUp:MasterKeyPath and GroundUp:MasterKey are configured; using file path.");
        }

        if (hasFilePath)
        {
            return ResolveFromFile(filePath!);
        }

        if (hasMasterKey)
        {
            return DecodeAndValidate(masterKeyValue!);
        }

        throw new InvalidOperationException(
            "Neither GroundUp:MasterKeyPath nor GroundUp:MasterKey is configured. " +
            "One must be set to provide the encryption master key.");
    }

    private byte[] ResolveFromFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Master key file not found at path '{path}'.");
        }

        var content = File.ReadAllText(path).Trim();

        if (string.IsNullOrEmpty(content))
        {
            throw new InvalidOperationException(
                $"Master key file at '{path}' is empty after trimming whitespace.");
        }

        CheckUnixFilePermissions(path);

        return DecodeAndValidate(content);
    }

    private static byte[] DecodeAndValidate(string base64Value)
    {
        byte[] decoded;

        try
        {
            decoded = Convert.FromBase64String(base64Value);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                "Master key value is not valid base64. Expected a base64-encoded 32-byte key.");
        }

        if (decoded.Length < 32)
        {
            throw new InvalidOperationException(
                $"Master key is {decoded.Length} bytes after decoding; minimum 32 bytes (256 bits) required.");
        }

        return decoded;
    }

    private void CheckUnixFilePermissions(string path)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return;

        try
        {
            var mode = File.GetUnixFileMode(path);

            const UnixFileMode permissiveBits =
                UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

            if ((mode & permissiveBits) != 0)
            {
                _logger.LogWarning(
                    "Master key file at '{Path}' has permissive file mode {Mode}; recommend 0600 for production.",
                    path,
                    mode);
            }
        }
        catch (Exception ex)
        {
            // If we can't read file permissions, log at debug and continue — don't block startup.
            _logger.LogDebug(ex, "Unable to check file permissions for master key file at '{Path}'.", path);
        }
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SecureHostCore.Storage;

/// <summary>
/// Secure encrypted storage for sensitive configuration and policy data
/// Uses DPAPI (Data Protection API) with machine-level encryption
/// </summary>
public sealed class SecureStorage
{
    private readonly ILogger<SecureStorage> _logger;
    private readonly string _storagePath;
    private readonly byte[] _entropy;
    private readonly SemaphoreSlim _lockSemaphore;

    public SecureStorage(ILogger<SecureStorage> logger, string storagePath)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _storagePath = storagePath ?? throw new ArgumentNullException(nameof(storagePath));
        _lockSemaphore = new SemaphoreSlim(1, 1);

        // Generate entropy from machine-specific data
        _entropy = GenerateEntropy();

        // Ensure storage directory exists
        Directory.CreateDirectory(Path.GetDirectoryName(_storagePath)!);

        _logger.LogInformation("Secure storage initialized at: {StoragePath}", _storagePath);
    }

    /// <summary>
    /// Saves data securely to disk
    /// </summary>
    public async Task SaveAsync<T>(string key, T data)
    {
        ArgumentNullException.ThrowIfNull(data);

        await _lockSemaphore.WaitAsync();
        try
        {
            // Serialize to JSON
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = false
            });

            var plaintext = Encoding.UTF8.GetBytes(json);

            // Encrypt using DPAPI
            var encrypted = System.Security.Cryptography.ProtectedData.Protect(
                plaintext,
                _entropy,
                DataProtectionScope.LocalMachine);

            // Generate HMAC for integrity verification
            var hmac = ComputeHmac(encrypted);

            // Combine HMAC + encrypted data
            var combined = new byte[hmac.Length + encrypted.Length];
            Buffer.BlockCopy(hmac, 0, combined, 0, hmac.Length);
            Buffer.BlockCopy(encrypted, 0, combined, hmac.Length, encrypted.Length);

            // Write to file
            var filePath = GetFilePath(key);
            await File.WriteAllBytesAsync(filePath, combined);

            _logger.LogDebug("Saved encrypted data for key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving secure data for key: {Key}", key);
            throw;
        }
        finally
        {
            _lockSemaphore.Release();
        }
    }

    /// <summary>
    /// Loads data securely from disk
    /// </summary>
    public async Task<T?> LoadAsync<T>(string key)
    {
        await _lockSemaphore.WaitAsync();
        try
        {
            var filePath = GetFilePath(key);
            if (!File.Exists(filePath))
            {
                _logger.LogDebug("Secure storage file not found for key: {Key}", key);
                return default;
            }

            var combined = await File.ReadAllBytesAsync(filePath);

            // Extract HMAC and encrypted data
            const int hmacSize = 32; // SHA256 size
            if (combined.Length <= hmacSize)
            {
                _logger.LogWarning("Invalid secure storage file for key: {Key}", key);
                return default;
            }

            var hmac = new byte[hmacSize];
            var encrypted = new byte[combined.Length - hmacSize];

            Buffer.BlockCopy(combined, 0, hmac, 0, hmacSize);
            Buffer.BlockCopy(combined, hmacSize, encrypted, 0, encrypted.Length);

            // Verify HMAC
            var computedHmac = ComputeHmac(encrypted);
            if (!hmac.SequenceEqual(computedHmac))
            {
                _logger.LogError("HMAC verification failed for key: {Key} - possible tampering detected", key);
                throw new CryptographicException("Data integrity check failed");
            }

            // Decrypt using DPAPI
            var plaintext = System.Security.Cryptography.ProtectedData.Unprotect(
                encrypted,
                _entropy,
                DataProtectionScope.LocalMachine);

            var json = Encoding.UTF8.GetString(plaintext);

            // Deserialize
            var data = JsonSerializer.Deserialize<T>(json);

            _logger.LogDebug("Loaded encrypted data for key: {Key}", key);
            return data;
        }
        catch (CryptographicException)
        {
            throw; // Re-throw crypto exceptions
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading secure data for key: {Key}", key);
            return default;
        }
        finally
        {
            _lockSemaphore.Release();
        }
    }

    /// <summary>
    /// Deletes secure data
    /// </summary>
    public async Task DeleteAsync(string key)
    {
        await _lockSemaphore.WaitAsync();
        try
        {
            var filePath = GetFilePath(key);
            if (File.Exists(filePath))
            {
                // Overwrite with random data before deletion (secure delete)
                var fileInfo = new FileInfo(filePath);
                var size = fileInfo.Length;

                await using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Write))
                {
                    var random = new byte[size];
                    RandomNumberGenerator.Fill(random);
                    await fs.WriteAsync(random);
                    await fs.FlushAsync();
                }

                File.Delete(filePath);
                _logger.LogInformation("Deleted secure data for key: {Key}", key);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting secure data for key: {Key}", key);
            throw;
        }
        finally
        {
            _lockSemaphore.Release();
        }
    }

    /// <summary>
    /// Checks if key exists
    /// </summary>
    public bool Exists(string key)
    {
        return File.Exists(GetFilePath(key));
    }

    /// <summary>
    /// Lists all keys
    /// </summary>
    public IEnumerable<string> ListKeys()
    {
        var directory = Path.GetDirectoryName(_storagePath)!;
        if (!Directory.Exists(directory))
            return Enumerable.Empty<string>();

        var pattern = Path.GetFileName(_storagePath);
        var baseName = pattern.Replace("*", "");

        return Directory.GetFiles(directory, "*.dat")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name != null)
            .Cast<string>();
    }

    /// <summary>
    /// Gets file path for a key
    /// </summary>
    private string GetFilePath(string key)
    {
        // Sanitize key for filename
        var sanitized = string.Join("_", key.Split(Path.GetInvalidFileNameChars()));
        var directory = Path.GetDirectoryName(_storagePath)!;
        return Path.Combine(directory, $"{sanitized}.dat");
    }

    /// <summary>
    /// Generates entropy from machine-specific data
    /// </summary>
    private static byte[] GenerateEntropy()
    {
        var machineId = Environment.MachineName;
        var osVersion = Environment.OSVersion.ToString();
        var combined = $"{machineId}|{osVersion}|SecureHost";

        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
    }

    /// <summary>
    /// Computes HMAC-SHA256 for integrity verification
    /// </summary>
    private byte[] ComputeHmac(byte[] data)
    {
        using var hmac = new HMACSHA256(_entropy);
        return hmac.ComputeHash(data);
    }
}

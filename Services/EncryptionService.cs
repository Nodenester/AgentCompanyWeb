using System.Security.Cryptography;
using System.Text;

namespace AgentCompanyWeb.Services;

/// <summary>
/// Service for encrypting/decrypting sensitive data like API keys.
/// </summary>
public class EncryptionService
{
    private readonly byte[] _key;

    public EncryptionService(IConfiguration configuration)
    {
        // Get or generate encryption key
        var keyString = configuration["Encryption:Key"];
        if (string.IsNullOrEmpty(keyString))
        {
            // In production, this should come from secure configuration
            keyString = "AgentCompanyDefaultKey123456789!";
        }

        // Derive a 256-bit key from the string
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(keyString));
    }

    /// <summary>
    /// Encrypt a plaintext string.
    /// </summary>
    public string? Encrypt(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return null;

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertextBytes = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

        // Prepend IV to ciphertext
        var result = new byte[aes.IV.Length + ciphertextBytes.Length];
        aes.IV.CopyTo(result, 0);
        ciphertextBytes.CopyTo(result, aes.IV.Length);

        return Convert.ToBase64String(result);
    }

    /// <summary>
    /// Decrypt an encrypted string.
    /// </summary>
    public string? Decrypt(string? ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
            return null;

        try
        {
            var fullBytes = Convert.FromBase64String(ciphertext);

            using var aes = Aes.Create();
            aes.Key = _key;

            // Extract IV from beginning
            var iv = new byte[aes.BlockSize / 8];
            var ciphertextBytes = new byte[fullBytes.Length - iv.Length];
            Array.Copy(fullBytes, 0, iv, 0, iv.Length);
            Array.Copy(fullBytes, iv.Length, ciphertextBytes, 0, ciphertextBytes.Length);

            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            var plaintextBytes = decryptor.TransformFinalBlock(ciphertextBytes, 0, ciphertextBytes.Length);

            return Encoding.UTF8.GetString(plaintextBytes);
        }
        catch
        {
            return null;
        }
    }
}

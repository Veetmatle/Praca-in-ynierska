using System.Security.Cryptography;
using System.Text;

namespace StudentApp.Api.Security;

/// <summary>
/// AES-GCM encryption for protecting API keys stored in the database.
/// Decryption happens only in-memory when the key is needed for an external API call.
/// </summary>
public interface IEncryptionService
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
}

public sealed class AesGcmEncryptionService : IEncryptionService
{
    private readonly byte[] _key;

    public AesGcmEncryptionService(IConfiguration config)
    {
        var hexKey = config["Encryption:Key"]
            ?? throw new InvalidOperationException("Encryption:Key is not configured.");
        _key = Convert.FromHexString(hexKey);
        
        if (_key.Length != 32)
            throw new InvalidOperationException("Encryption key must be 256-bit (32 bytes / 64 hex chars).");
    }

    public string Encrypt(string plaintext)
    {
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[AesGcm.NonceByteSizes.MaxSize]; // 12 bytes
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize]; // 16 bytes

        using var aes = new AesGcm(_key, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Format: base64(nonce + ciphertext + tag)
        var result = new byte[nonce.Length + ciphertext.Length + tag.Length];
        nonce.CopyTo(result, 0);
        ciphertext.CopyTo(result, nonce.Length);
        tag.CopyTo(result, nonce.Length + ciphertext.Length);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string encoded)
    {
        var data = Convert.FromBase64String(encoded);

        const int nonceSize = 12;
        const int tagSize = 16;
        var ciphertextSize = data.Length - nonceSize - tagSize;

        if (ciphertextSize < 0)
            throw new CryptographicException("Invalid encrypted data.");

        var nonce = data[..nonceSize];
        var ciphertext = data[nonceSize..(nonceSize + ciphertextSize)];
        var tag = data[(nonceSize + ciphertextSize)..];

        var plaintext = new byte[ciphertextSize];
        using var aes = new AesGcm(_key, AesGcm.TagByteSizes.MaxSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }
}

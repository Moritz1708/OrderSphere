using System.Security.Cryptography;
using System.Text;

namespace OrderSphere.Partners.Domain;

/// <summary>
/// Generates and hashes partner API keys. Only the SHA-256 hash is ever persisted
/// (<see cref="Entities.Partner.ApiKeyHash"/>) — the raw key is returned to the caller
/// exactly once, at issue/rotation time, and cannot be recovered afterward.
/// </summary>
public static class ApiKeyGenerator
{
    private const string Prefix = "ps_";

    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Prefix + Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static string Hash(string rawKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexString(bytes);
    }
}

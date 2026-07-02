using System.Security.Cryptography;
using System.Text;

namespace OrderSphere.ApiGateway.Authentication;

/// <summary>
/// Hashes an incoming <c>X-API-Key</c> header value the same way
/// <c>OrderSphere.Partners.Domain.ApiKeyGenerator.Hash</c> hashes it before storing it —
/// duplicated rather than shared via a project reference because the gateway must not take a
/// project dependency on a service's domain layer (see CLAUDE.md, no cross-service references).
/// </summary>
internal static class ApiKeyHasher
{
    public static string Hash(string rawKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexString(bytes);
    }
}

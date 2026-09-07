using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using OrderSphere.Bff.Logging;

namespace OrderSphere.Bff.Auth;

/// <summary>
/// ITicketStore backed by Redis via IDistributedCache.
/// Tickets are serialized with TicketSerializer, then protected (AES-256) with
/// DataProtection before storage. This enables safe multi-instance BFF deployments
/// and allows individual session revocation for back-channel logout (Phase 4).
/// </summary>
public sealed class RedisTicketStore : ITicketStore
{
    private const string KeyPrefix = "bff:session:";
    /// <summary>
    /// Secondary index: bff:sid:{sessionId} → session key.
    /// Written on StoreAsync so BackchannelLogoutEndpoint can revoke sessions by Auth0 sid.
    /// Auth0 sends session_state (= session ID) in ID tokens and sid in logout_tokens.
    /// </summary>
    private const string SidPrefix = "bff:sid:";

    private readonly IDistributedCache _cache;
    private readonly IDataProtector _protector;
    private readonly ILogger<RedisTicketStore> _logger;

    public RedisTicketStore(
        IDistributedCache cache,
        IDataProtectionProvider dataProtection,
        ILogger<RedisTicketStore> logger)
    {
        _cache = cache;
        _protector = dataProtection.CreateProtector("OrderSphere.Bff.SessionTicket");
        _logger = logger;
    }

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = KeyPrefix + Guid.NewGuid().ToString("N");
        await RenewAsync(key, ticket);

        // Write secondary index sid → session key so BackchannelLogoutEndpoint can revoke
        // a session given only the Auth0 session_state / sid claim from the logout_token.
        var sid = ticket.Principal.FindFirst("session_state")?.Value
               ?? ticket.Principal.FindFirst("sid")?.Value;
        if (!string.IsNullOrEmpty(sid))
        {
            var sidOptions = new DistributedCacheEntryOptions();
            if (ticket.Properties.ExpiresUtc is { } exp)
                sidOptions.AbsoluteExpiration = exp;
            else
                sidOptions.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(8);

            await _cache.SetStringAsync(SidPrefix + sid, key, sidOptions);
            _logger.SidIndexWritten(sid, key);
        }

        _logger.SessionTicketStored(key);
        return key;
    }

    /// <summary>
    /// Looks up the session key for the given Auth0 session ID (sid / session_state).
    /// Returns null when the session is not found or has already expired.
    /// </summary>
    public Task<string?> FindKeyBySessionIdAsync(string sid)
        => _cache.GetStringAsync(SidPrefix + sid);

    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        var plain = TicketSerializer.Default.Serialize(ticket);
        var ciphertext = _protector.Protect(plain);

        var options = new DistributedCacheEntryOptions();
        if (ticket.Properties.ExpiresUtc is { } exp)
            options.AbsoluteExpiration = exp;
        else
            options.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(8);

        await _cache.SetAsync(key, ciphertext, options);
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        var ciphertext = await _cache.GetAsync(key);
        if (ciphertext is null)
            return null;

        try
        {
            var plain = _protector.Unprotect(ciphertext);
            return TicketSerializer.Default.Deserialize(plain);
        }
        catch (Exception ex)
        {
            _logger.SessionTicketUnprotectFailed(ex, key);
            return null;
        }
    }

    public Task RemoveAsync(string key)
    {
        _logger.SessionTicketRemoved(key);
        return _cache.RemoveAsync(key);
    }
}

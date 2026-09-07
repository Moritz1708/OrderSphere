using OrderSphere.BuildingBlocks.Compliance;

namespace OrderSphere.Bff.Logging;

/// <summary>
/// Source-generated log methods for the BFF session and back-channel logout paths.
/// EventIds 12001-12099 (Gateways range 12000-12999, see docs/logging.md).
/// <para>
/// These exist because redaction only applies to <c>[LoggerMessage]</c> parameters carrying a
/// classification attribute — a plain <c>logger.LogDebug("... {Sid}", sid)</c> reaches every sink
/// verbatim. Both identifiers here are T2: the Auth0 <c>sid</c> identifies a person via a join,
/// and the Redis session key is the handle that dereferences to that person's live session.
/// Hashing keeps them groupable (all records for one session still line up) without putting a
/// usable session handle in the log stream.
/// </para>
/// </summary>
internal static partial class BffLog
{
    [LoggerMessage(
        EventId = 12001,
        Level = LogLevel.Debug,
        Message = "Session id index written.")]
    public static partial void SidIndexWritten(
        this ILogger logger,
        [PseudonymousId] string sid,
        [PseudonymousId] string sessionKey);

    [LoggerMessage(
        EventId = 12002,
        Level = LogLevel.Debug,
        Message = "Session ticket stored.")]
    public static partial void SessionTicketStored(
        this ILogger logger,
        [PseudonymousId] string sessionKey);

    [LoggerMessage(
        EventId = 12003,
        Level = LogLevel.Debug,
        Message = "Session ticket removed.")]
    public static partial void SessionTicketRemoved(
        this ILogger logger,
        [PseudonymousId] string sessionKey);

    [LoggerMessage(
        EventId = 12004,
        Level = LogLevel.Warning,
        Message = "Failed to unprotect session ticket. Treating as missing.")]
    public static partial void SessionTicketUnprotectFailed(
        this ILogger logger,
        Exception exception,
        [PseudonymousId] string sessionKey);

    [LoggerMessage(
        EventId = 12005,
        Level = LogLevel.Information,
        Message = "Back-channel logout: no active session found (already expired or logged out).")]
    public static partial void BackchannelLogoutNoActiveSession(
        this ILogger logger,
        [PseudonymousId] string sid);

    [LoggerMessage(
        EventId = 12006,
        Level = LogLevel.Information,
        Message = "Back-channel logout: session revoked.")]
    public static partial void BackchannelLogoutSessionRevoked(
        this ILogger logger,
        [PseudonymousId] string sid,
        [PseudonymousId] string sessionKey);
}

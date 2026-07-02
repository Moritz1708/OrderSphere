namespace OrderSphere.BuildingBlocks.StronglyTypedIds;

/// <summary>
/// Derivation helper for the <c>TenantId</c> column added by ADR 0012. Mirrors
/// <see cref="CustomerId.FromSub"/>: deterministic, no shared registry required.
/// </summary>
public static class TenantId
{
    /// <summary>
    /// Tenant used for rows with no organisation membership (backfilled default for pre-existing
    /// data, and the resolved value for callers whose token carries no <c>org_id</c> claim).
    /// </summary>
    public static Guid Default => Guid.Empty;

    // Auth0 org_id format is "org_<opaque_id>", not a UUID. Derive a stable,
    // deterministic GUID via SHA256 so the same organisation always maps to the same TenantId.
    public static Guid FromOrgId(string orgId)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(orgId));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50); // RFC 4122 version 5
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(hash[..16]);
    }
}

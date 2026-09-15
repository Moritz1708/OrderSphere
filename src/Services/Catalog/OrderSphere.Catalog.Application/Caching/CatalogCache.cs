namespace OrderSphere.Catalog.Application.Caching;

internal static class CatalogCache
{
    public const string Tag = "catalog";

    /// <summary>
    /// Product-by-slug entry. The key carries the tenant because the same slug
    /// resolves to different rows per tenant (and to the default tenant's row for
    /// anonymous callers); a shared key would leak one tenant's product to another.
    /// </summary>
    public static string ProductBySlugKey(Guid tenantId, string slug) => $"catalog:{tenantId:N}:product:slug:{slug}";
}

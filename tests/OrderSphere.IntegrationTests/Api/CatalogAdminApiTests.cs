using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace OrderSphere.IntegrationTests.Api;

/// <summary>
/// Drives the Catalog admin CRUD, the service-to-service internal endpoints (stock + reservation
/// saga) and the authenticated review flow against the in-memory SQLite host. Uses a dedicated
/// factory instance so its seeded state is isolated from <see cref="CatalogApiTests"/>.
/// </summary>
public sealed class CatalogAdminApiTests : IClassFixture<CatalogApiFactory>
{
    private readonly CatalogApiFactory _factory;

    public CatalogAdminApiTests(CatalogApiFactory factory) => _factory = factory;

    private HttpClient Admin()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, "auth0|catalog-admin");
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, "admin");
        return client;
    }

    private HttpClient Customer(string sub)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, sub);
        return client;
    }

    /// <summary>D4 — internal endpoints require an authenticated caller (any client-credentials
    /// token) but no role; simulates the M2M identity Ordering's HttpCatalogClient authenticates as.</summary>
    private HttpClient InternalCaller()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, "service|ordering");
        return client;
    }

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCategoryAsync(HttpClient admin) =>
        await CreatedIdAsync(await admin.PostAsJsonAsync("api/v1/admin/categories",
            new { name = $"Cat-{Guid.NewGuid():N}", description = "A category" }));

    private async Task<Guid> CreateProductAsync(HttpClient admin, Guid categoryId, int stock = 100) =>
        await CreatedIdAsync(await admin.PostAsJsonAsync("api/v1/admin/products", new
        {
            name = $"Product-{Guid.NewGuid():N}",
            description = "A product",
            price = 24.99m,
            stock,
            categoryId,
            sku = $"SKU-{Guid.NewGuid():N}".Substring(0, 12),
            isActive = true,
        }));


    [Fact]
    public async Task Brand_crud_roundtrip()
    {
        var admin = Admin();

        var id = await CreatedIdAsync(await admin.PostAsJsonAsync("api/v1/admin/brands",
            new { name = $"Brand-{Guid.NewGuid():N}", description = "A brand" }));

        var update = await admin.PutAsJsonAsync($"api/v1/admin/brands/{id}",
            new { name = "Renamed Brand", description = "Updated", isActive = true });
        update.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var delete = await admin.DeleteAsync($"api/v1/admin/brands/{id}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Category_crud_roundtrip()
    {
        var admin = Admin();
        var id = await CreateCategoryAsync(admin);

        var update = await admin.PutAsJsonAsync($"api/v1/admin/categories/{id}",
            new { name = "Renamed Category", description = "Updated", isActive = true });
        update.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var delete = await admin.DeleteAsync($"api/v1/admin/categories/{id}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Product_lifecycle_create_read_update_delete()
    {
        var admin = Admin();
        var categoryId = await CreateCategoryAsync(admin);
        var productId = await CreateProductAsync(admin, categoryId);

        var byId = await admin.GetAsync($"api/v1/admin/products/{productId}");
        byId.StatusCode.Should().Be(HttpStatusCode.OK);

        var update = await admin.PutAsJsonAsync($"api/v1/admin/products/{productId}", new
        {
            name = "Renamed Product",
            description = "Updated",
            price = 29.99m,
            stock = 50,
            categoryId,
            sku = "SKU-UPDATED",
            isActive = true,
        });
        update.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var delete = await admin.DeleteAsync($"api/v1/admin/products/{productId}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var gone = await admin.GetAsync($"api/v1/admin/products/{productId}");
        gone.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reindex_search_reports_unavailable_when_search_is_disabled()
    {
        // The test host runs with Azure AI Search unconfigured (disabled index), so the command
        // fails closed with SearchUnavailable → 400 rather than silently reporting success.
        var response = await Admin().PostAsync("api/v1/admin/products/reindex-search", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }


    [Fact]
    public async Task Internal_product_endpoints_read_and_adjust_stock()
    {
        var admin = Admin();
        var categoryId = await CreateCategoryAsync(admin);
        var productId = await CreateProductAsync(admin, categoryId, stock: 40);

        var internalCaller = InternalCaller();

        (await internalCaller.GetAsync($"internal/products/{productId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await internalCaller.GetAsync($"internal/products/names?ids={productId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await internalCaller.GetAsync($"internal/products/infos?ids={productId}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var decrement = await internalCaller.PostAsJsonAsync($"internal/products/{productId}/decrement-stock", new { quantity = 10 });
        decrement.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var restore = await internalCaller.PostAsJsonAsync($"internal/products/{productId}/restore-stock", new { quantity = 5 });
        restore.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await internalCaller.GetAsync($"internal/products/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Internal_endpoints_reject_anonymous_callers()
    {
        var admin = Admin();
        var categoryId = await CreateCategoryAsync(admin);
        var productId = await CreateProductAsync(admin, categoryId, stock: 40);

        var anon = _factory.CreateClient();

        (await anon.GetAsync($"internal/products/{productId}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.PostAsJsonAsync("internal/reservations",
            new { correlationId = Guid.NewGuid(), items = new[] { new { productId, quantity = 1 } } }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reservation_saga_reserve_confirm_and_release()
    {
        var admin = Admin();
        var categoryId = await CreateCategoryAsync(admin);
        var productId = await CreateProductAsync(admin, categoryId, stock: 10);
        var internalCaller = InternalCaller();

        // Reserve within stock, then confirm (deducts stock).
        var corr1 = Guid.NewGuid();
        var reserve = await internalCaller.PostAsJsonAsync("internal/reservations",
            new { correlationId = corr1, items = new[] { new { productId, quantity = 3 } } });
        reserve.StatusCode.Should().Be(HttpStatusCode.OK);

        var confirm = await internalCaller.PostAsync($"internal/reservations/{corr1}/confirm", content: null);
        confirm.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Reserve again, then release (returns the hold without deducting).
        var corr2 = Guid.NewGuid();
        await internalCaller.PostAsJsonAsync("internal/reservations",
            new { correlationId = corr2, items = new[] { new { productId, quantity = 2 } } });
        var release = await internalCaller.PostAsync($"internal/reservations/{corr2}/release", content: null);
        release.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Reservation_beyond_available_stock_conflicts()
    {
        var admin = Admin();
        var categoryId = await CreateCategoryAsync(admin);
        var productId = await CreateProductAsync(admin, categoryId, stock: 2);
        var internalCaller = InternalCaller();

        var response = await internalCaller.PostAsJsonAsync("internal/reservations",
            new { correlationId = Guid.NewGuid(), items = new[] { new { productId, quantity = 99 } } });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }


    [Fact]
    public async Task Public_product_reads_by_slug_batch_and_stock_adjustment()
    {
        var admin = Admin();
        var categoryId = await CreateCategoryAsync(admin);
        var productId = await CreateProductAsync(admin, categoryId, stock: 30);

        var anon = _factory.CreateClient();

        // The public listing surfaces the slug used by the by-slug endpoint.
        using var listing = await anon.GetFromJsonAsync<JsonDocument>("api/v1/products?page=1&pageSize=50");
        var created = listing!.RootElement.GetProperty("items").EnumerateArray()
            .First(p => p.GetProperty("id").GetGuid() == productId);
        var slug = created.GetProperty("slug").GetString();

        (await anon.GetAsync($"api/v1/products/{slug}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await anon.GetAsync($"api/v1/products/batch?ids={productId}")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Stock adjustment is an admin operation: anonymous callers are refused, a signed-in
        // customer is forbidden, and only the catalog admin role goes through.
        (await anon.PostAsJsonAsync($"api/v1/products/{productId}/stock/decrement", new { quantity = 5 }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Customer("auth0|shopper").PostAsJsonAsync($"api/v1/products/{productId}/stock/decrement", new { quantity = 5 }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var decrement = await admin.PostAsJsonAsync($"api/v1/products/{productId}/stock/decrement", new { quantity = 5 });
        decrement.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var restore = await admin.PostAsJsonAsync($"api/v1/products/{productId}/stock/restore", new { quantity = 5 });
        restore.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }


    [Fact]
    public async Task Review_create_then_list_for_a_purchased_product()
    {
        var admin = Admin();
        var categoryId = await CreateCategoryAsync(admin);
        var productId = await CreateProductAsync(admin, categoryId);

        var create = await Customer("auth0|reviewer").PostAsJsonAsync(
            $"api/v1/reviews/product/{productId}",
            new { rating = 5, title = "Great", body = "Works perfectly." });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        // Public read of reviews for the product.
        var list = await _factory.CreateClient().GetAsync($"api/v1/reviews/product/{productId}");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Review_creation_requires_authentication()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            $"api/v1/reviews/product/{Guid.NewGuid()}",
            new { rating = 4, title = "x", body = "y" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

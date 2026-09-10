using NSubstitute;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using OrderSphere.Catalog.Application.Features.Products.Public.GetProductBySlug;
using OrderSphere.BuildingBlocks.Security;
using OrderSphere.Catalog.Tests.Helpers;

namespace OrderSphere.Catalog.Tests.Features.Products;

public sealed class GetProductBySlugQueryHandlerTests
{
    private static HybridCache NewCache()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        return services.BuildServiceProvider().GetRequiredService<HybridCache>();
    }

    [Fact]
    public async Task Handle_ExistingActiveProduct_ReturnsDto()
    {
        await using var ctx = CatalogDbContextFactory.Create();
        var category = new Category("Shoes");
        var product = new Product("Trail Runner", "desc", Money.Of(99m), 5, category.Id, "SKU-1");
        ctx.Categories.Add(category);
        ctx.Products.Add(product);
        await ctx.SaveChangesAsync();

        var result = await new GetProductBySlugQueryHandler(ctx, NewCache(), Substitute.For<ITenantContext>())
            .Handle(new(product.Slug), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Slug.Should().Be(product.Slug);
        result.Value.Name.Should().Be("Trail Runner");
    }

    [Fact]
    public async Task Handle_UnknownSlug_ReturnsNotFound()
    {
        await using var ctx = CatalogDbContextFactory.Create();

        var result = await new GetProductBySlugQueryHandler(ctx, NewCache(), Substitute.For<ITenantContext>())
            .Handle(new("does-not-exist"), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ProductErrors.NotFound);
    }

    [Fact]
    public async Task Handle_InactiveProduct_ReturnsNotFound()
    {
        await using var ctx = CatalogDbContextFactory.Create();
        var category = new Category("Shoes");
        var product = new Product("Trail Runner", "desc", Money.Of(99m), 5, category.Id, "SKU-1");
        product.Deactivate();
        ctx.Categories.Add(category);
        ctx.Products.Add(product);
        await ctx.SaveChangesAsync();

        var result = await new GetProductBySlugQueryHandler(ctx, NewCache(), Substitute.For<ITenantContext>())
            .Handle(new(product.Slug), default);

        result.IsFailure.Should().BeTrue();
    }
}

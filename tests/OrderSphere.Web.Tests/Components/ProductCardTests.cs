using Bunit;
using Microsoft.AspNetCore.Components;
using OrderSphere.Web.Components;

namespace OrderSphere.Web.Tests.Components;

public sealed class ProductCardTests : BunitBase
{
    private static ProductDto MakeProduct(int stock = 5, int reviewCount = 0, double avgRating = 0.0)
        => new(
            Guid.NewGuid(),
            "Test Widget",
            "test-widget",
            "A fine product",
            19.99m,
            stock,
            Guid.NewGuid(),
            "Electronics",
            "SKU-01",
            null,
            true,
            avgRating,
            reviewCount);


    [Fact]
    public void ProductName_RenderedInMarkup()
    {
        var cut = Render<ProductCard>(p => p.Add(c => c.Product, MakeProduct()));

        cut.Markup.Should().Contain("Test Widget");
    }

    [Fact]
    public void ProductPrice_RenderedInMarkup()
    {
        var cut = Render<ProductCard>(p => p.Add(c => c.Product, MakeProduct()));

        cut.Markup.Should().Contain("19");
    }


    [Fact]
    public void SoldOutChip_Shown_WhenStockIsZero()
    {
        var cut = Render<ProductCard>(p => p.Add(c => c.Product, MakeProduct(stock: 0)));

        cut.Markup.Should().Contain("Common.SoldOut");
    }

    [Fact]
    public void SoldOutChip_Hidden_WhenStockGreaterThanZero()
    {
        var cut = Render<ProductCard>(p => p.Add(c => c.Product, MakeProduct(stock: 10)));

        cut.Markup.Should().NotContain("Common.SoldOut");
    }


    [Fact]
    public void StarRating_Shown_WhenReviewCountGreaterThanZero()
    {
        var cut = Render<ProductCard>(p =>
            p.Add(c => c.Product, MakeProduct(reviewCount: 5, avgRating: 4.2)));

        // StarRating renders its aria-label attribute containing the localizer key
        cut.Markup.Should().Contain("Reviews.RatingAria");
    }

    [Fact]
    public void StarRating_Hidden_WhenReviewCountIsZero()
    {
        var cut = Render<ProductCard>(p =>
            p.Add(c => c.Product, MakeProduct(reviewCount: 0)));

        cut.Markup.Should().NotContain("Reviews.RatingAria");
    }


    [Fact]
    public void OnOpen_Invoked_WhenCardClicked()
    {
        ProductDto? opened = null;
        var product = MakeProduct();

        var cut = Render<ProductCard>(p =>
        {
            p.Add(c => c.Product, product);
            p.Add(c => c.OnOpen, EventCallback.Factory.Create<ProductDto>(this, dto => opened = dto));
        });

        cut.Find(".os-product-card").Click();

        opened.Should().Be(product);
    }

    [Fact]
    public void OnAdd_Invoked_WhenAddButtonClicked()
    {
        ProductDto? added = null;
        var product = MakeProduct(stock: 5);

        var cut = Render<ProductCard>(p =>
        {
            p.Add(c => c.Product, product);
            p.Add(c => c.OnAdd, EventCallback.Factory.Create<ProductDto>(this, dto => added = dto));
        });

        cut.Find(".product-add-btn").Click();

        added.Should().Be(product);
    }

    [Fact]
    public void AddButton_Disabled_WhenStockIsZero()
    {
        var cut = Render<ProductCard>(p =>
            p.Add(c => c.Product, MakeProduct(stock: 0)));

        var button = cut.Find(".product-add-btn");
        button.HasAttribute("disabled").Should().BeTrue();
    }
}

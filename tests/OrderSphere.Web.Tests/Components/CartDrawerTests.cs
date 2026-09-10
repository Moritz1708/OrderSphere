using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using OrderSphere.Web.Components;

namespace OrderSphere.Web.Tests.Components;

public sealed class CartDrawerTests : BunitBase
{
    private IOrderingClient SetupCart(CartDto cart)
    {
        var client = Substitute.For<IOrderingClient>();
        client.GetCartAsync(Arg.Any<CancellationToken>())
            .Returns(ApiResult<CartDto>.Ok(cart));
        Services.AddSingleton(client);
        Services.AddSingleton<CartState>();
        return client;
    }


    [Fact]
    public async Task EmptyCart_ShowsEmptyMessage()
    {
        SetupCart(new CartDto(Guid.NewGuid(), []));
        await Services.GetRequiredService<CartState>().RefreshAsync();

        var cut = Render<CartDrawer>(p => p.Add(c => c.IsOpen, true));

        cut.Markup.Should().Contain("Cart.Empty");
    }

    [Fact]
    public async Task EmptyCart_DoesNotShowTotalOrCheckoutButton()
    {
        SetupCart(new CartDto(Guid.NewGuid(), []));
        await Services.GetRequiredService<CartState>().RefreshAsync();

        var cut = Render<CartDrawer>(p => p.Add(c => c.IsOpen, true));

        cut.Markup.Should().NotContain("Cart.Total");
        cut.Markup.Should().NotContain("Cart.ToCheckout");
    }


    [Fact]
    public async Task CartWithItems_ShowsProductName()
    {
        var item = new CartItemDto(Guid.NewGuid(), "Widget Pro", 29.99m, 2);
        SetupCart(new CartDto(Guid.NewGuid(), [item]));
        await Services.GetRequiredService<CartState>().RefreshAsync();

        var cut = Render<CartDrawer>(p => p.Add(c => c.IsOpen, true));

        cut.Markup.Should().Contain("Widget Pro");
    }

    [Fact]
    public async Task CartWithItems_ShowsLineTotal()
    {
        var item = new CartItemDto(Guid.NewGuid(), "Widget", 10m, 3);
        SetupCart(new CartDto(Guid.NewGuid(), [item]));
        await Services.GetRequiredService<CartState>().RefreshAsync();

        var cut = Render<CartDrawer>(p => p.Add(c => c.IsOpen, true));

        // 10 × 3 = 30
        cut.Markup.Should().Contain(Formatting.Currency(30m));
    }


    [Fact]
    public async Task GoToCheckout_NavigatesToCheckoutAndClosesDrawer()
    {
        var item = new CartItemDto(Guid.NewGuid(), "Widget", 10m, 1);
        SetupCart(new CartDto(Guid.NewGuid(), [item]));
        await Services.GetRequiredService<CartState>().RefreshAsync();

        bool? isOpenChangedTo = null;
        var cut = Render<CartDrawer>(p =>
        {
            p.Add(c => c.IsOpen, true);
            p.Add(c => c.IsOpenChanged,
                EventCallback.Factory.Create<bool>(this, v => isOpenChangedTo = v));
        });

        cut.Find(".os-btn--primary").Click();

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.Uri.Should().EndWith("/checkout");
        isOpenChangedTo.Should().BeFalse();
    }

    [Fact]
    public async Task Stepper_Increase_AddsOneOfTheSameProduct()
    {
        var item = new CartItemDto(Guid.NewGuid(), "Widget", 10m, 2);
        var client = SetupCart(new CartDto(Guid.NewGuid(), [item]));
        client.AddToCartAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Ok());
        await Services.GetRequiredService<CartState>().RefreshAsync();

        var cut = Render<CartDrawer>(p => p.Add(c => c.IsOpen, true));
        await cut.FindAll(".os-qty__btn")[1].ClickAsync(new());

        await client.Received(1).AddToCartAsync(item.ProductId, 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Stepper_Decrease_DecrementsTheLine()
    {
        var item = new CartItemDto(Guid.NewGuid(), "Widget", 10m, 2);
        var client = SetupCart(new CartDto(Guid.NewGuid(), [item]));
        client.DecreaseCartItemAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Ok());
        await Services.GetRequiredService<CartState>().RefreshAsync();

        var cut = Render<CartDrawer>(p => p.Add(c => c.IsOpen, true));
        await cut.FindAll(".os-qty__btn")[0].ClickAsync(new());

        await client.Received(1).DecreaseCartItemAsync(item.ProductId, Arg.Any<CancellationToken>());
    }
}

using OrderSphere.Partners.Application.Features.Partners.CreatePartner;

namespace OrderSphere.Partners.Tests.Features;

public sealed class CreatePartnerCommandHandlerTests
{
    private static IPartnersDbContext MakeContext()
    {
        var partners = new List<Partner>().BuildMockDbSet();
        var ctx = Substitute.For<IPartnersDbContext>();
        ctx.Partners.Returns(partners);
        ctx.SaveChangesAsync(default).ReturnsForAnyArgs(1);
        return ctx;
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsPartnerIdAndRawApiKey()
    {
        var ctx = MakeContext();

        var result = await new CreatePartnerCommandHandler(ctx).Handle(
            new CreatePartnerCommand("Acme Corp", QuotaTier.Premium), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.PartnerId.Should().NotBeEmpty();
        result.Value.ApiKey.Should().StartWith("ps_");
    }

    [Fact]
    public async Task Handle_ValidRequest_AddsPartnerToContext()
    {
        var ctx = MakeContext();

        await new CreatePartnerCommandHandler(ctx).Handle(
            new CreatePartnerCommand("Acme Corp", QuotaTier.Standard), default);

        ctx.Partners.Received(1).Add(Arg.Any<Partner>());
        await ctx.Received(1).SaveChangesAsync(default);
    }
}

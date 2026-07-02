using OrderSphere.Partners.Application.Features.Partners.GetAllPartners;
using OrderSphere.Partners.Application.Features.Partners.GetPartner;

namespace OrderSphere.Partners.Tests.Features;

public sealed class GetPartnerQueryHandlerTests
{
    private static IPartnersDbContext MakeContext(params Partner[] seed)
    {
        var partners = seed.ToList().BuildMockDbSet();
        var ctx = Substitute.For<IPartnersDbContext>();
        ctx.Partners.Returns(partners);
        return ctx;
    }

    [Fact]
    public async Task GetPartner_ExistingPartner_ReturnsDto()
    {
        var (partner, _) = Partner.Create("Acme Corp", QuotaTier.Standard);
        var ctx = MakeContext(partner);

        var result = await new GetPartnerQueryHandler(ctx).Handle(
            new GetPartnerQuery(partner.Id.Value), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Acme Corp");
    }

    [Fact]
    public async Task GetPartner_UnknownPartner_ReturnsNotFound()
    {
        var ctx = MakeContext();

        var result = await new GetPartnerQueryHandler(ctx).Handle(
            new GetPartnerQuery(Guid.NewGuid()), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PartnerErrors.NotFound);
    }

    [Fact]
    public async Task GetAllPartners_ReturnsAllSeededPartners()
    {
        var (partnerA, _) = Partner.Create("Acme Corp", QuotaTier.Standard);
        var (partnerB, _) = Partner.Create("Globex", QuotaTier.Premium);
        var ctx = MakeContext(partnerA, partnerB);

        var result = await new GetAllPartnersQueryHandler(ctx).Handle(new GetAllPartnersQuery(), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }
}

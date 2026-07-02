using OrderSphere.Partners.Application.Features.Partners.GetPartnerByKeyHash;

namespace OrderSphere.Partners.Tests.Features;

public sealed class GetPartnerByKeyHashQueryHandlerTests
{
    private static IPartnersDbContext MakeContext(params Partner[] seed)
    {
        var partners = seed.ToList().BuildMockDbSet();
        var ctx = Substitute.For<IPartnersDbContext>();
        ctx.Partners.Returns(partners);
        return ctx;
    }

    [Fact]
    public async Task Handle_ActivePartnerWithMatchingHash_ReturnsLookupDto()
    {
        var (partner, apiKey) = Partner.Create("Acme Corp", QuotaTier.Premium);
        var ctx = MakeContext(partner);

        var result = await new GetPartnerByKeyHashQueryHandler(ctx).Handle(
            new GetPartnerByKeyHashQuery(ApiKeyGenerator.Hash(apiKey)), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.PartnerId.Should().Be(partner.Id.Value);
        result.Value.QuotaTier.Should().Be(nameof(QuotaTier.Premium));
    }

    [Fact]
    public async Task Handle_UnknownHash_ReturnsNotFound()
    {
        var ctx = MakeContext();

        var result = await new GetPartnerByKeyHashQueryHandler(ctx).Handle(
            new GetPartnerByKeyHashQuery("unknown-hash"), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PartnerErrors.NotFound);
    }

    [Fact]
    public async Task Handle_RevokedPartner_ReturnsNotFound()
    {
        var (partner, apiKey) = Partner.Create("Acme Corp", QuotaTier.Standard);
        partner.Revoke();
        var ctx = MakeContext(partner);

        var result = await new GetPartnerByKeyHashQueryHandler(ctx).Handle(
            new GetPartnerByKeyHashQuery(ApiKeyGenerator.Hash(apiKey)), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PartnerErrors.NotFound);
    }
}

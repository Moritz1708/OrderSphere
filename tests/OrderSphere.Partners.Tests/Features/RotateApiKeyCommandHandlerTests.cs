using OrderSphere.Partners.Application.Features.Partners.RotateApiKey;

namespace OrderSphere.Partners.Tests.Features;

public sealed class RotateApiKeyCommandHandlerTests
{
    private static IPartnersDbContext MakeContext(params Partner[] seed)
    {
        var partners = seed.ToList().BuildMockDbSet();
        var ctx = Substitute.For<IPartnersDbContext>();
        ctx.Partners.Returns(partners);
        ctx.SaveChangesAsync(default).ReturnsForAnyArgs(1);
        return ctx;
    }

    [Fact]
    public async Task Handle_ExistingActivePartner_ReturnsNewApiKey()
    {
        var (partner, originalKey) = Partner.Create("Acme Corp", QuotaTier.Standard);
        var ctx = MakeContext(partner);

        var result = await new RotateApiKeyCommandHandler(ctx).Handle(
            new RotateApiKeyCommand(partner.Id.Value), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.ApiKey.Should().NotBe(originalKey);
    }

    [Fact]
    public async Task Handle_UnknownPartner_ReturnsNotFound()
    {
        var ctx = MakeContext();

        var result = await new RotateApiKeyCommandHandler(ctx).Handle(
            new RotateApiKeyCommand(Guid.NewGuid()), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PartnerErrors.NotFound);
    }

    [Fact]
    public async Task Handle_RevokedPartner_ReturnsAlreadyRevoked()
    {
        var (partner, _) = Partner.Create("Acme Corp", QuotaTier.Standard);
        partner.Revoke();
        var ctx = MakeContext(partner);

        var result = await new RotateApiKeyCommandHandler(ctx).Handle(
            new RotateApiKeyCommand(partner.Id.Value), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PartnerErrors.AlreadyRevoked);
    }
}

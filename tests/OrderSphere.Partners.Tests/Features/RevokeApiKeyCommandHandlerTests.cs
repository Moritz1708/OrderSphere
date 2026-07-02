using OrderSphere.Partners.Application.Features.Partners.RevokeApiKey;

namespace OrderSphere.Partners.Tests.Features;

public sealed class RevokeApiKeyCommandHandlerTests
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
    public async Task Handle_ExistingActivePartner_RevokesAndSucceeds()
    {
        var (partner, _) = Partner.Create("Acme Corp", QuotaTier.Standard);
        var ctx = MakeContext(partner);

        var result = await new RevokeApiKeyCommandHandler(ctx).Handle(
            new RevokeApiKeyCommand(partner.Id.Value), default);

        result.IsSuccess.Should().BeTrue();
        partner.Status.Should().Be(PartnerStatus.Revoked);
    }

    [Fact]
    public async Task Handle_UnknownPartner_ReturnsNotFound()
    {
        var ctx = MakeContext();

        var result = await new RevokeApiKeyCommandHandler(ctx).Handle(
            new RevokeApiKeyCommand(Guid.NewGuid()), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PartnerErrors.NotFound);
    }

    [Fact]
    public async Task Handle_AlreadyRevokedPartner_ReturnsAlreadyRevoked()
    {
        var (partner, _) = Partner.Create("Acme Corp", QuotaTier.Standard);
        partner.Revoke();
        var ctx = MakeContext(partner);

        var result = await new RevokeApiKeyCommandHandler(ctx).Handle(
            new RevokeApiKeyCommand(partner.Id.Value), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PartnerErrors.AlreadyRevoked);
    }
}

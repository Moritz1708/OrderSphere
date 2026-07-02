namespace OrderSphere.Partners.Tests.Domain;

public sealed class PartnerTests
{
    [Fact]
    public void Create_ReturnsActivePartnerAndRawApiKey()
    {
        var (partner, apiKey) = Partner.Create("Acme Corp", QuotaTier.Standard);

        partner.Name.Should().Be("Acme Corp");
        partner.QuotaTier.Should().Be(QuotaTier.Standard);
        partner.Status.Should().Be(PartnerStatus.Active);
        apiKey.Should().StartWith("ps_");
        partner.KeyPrefix.Should().Be(apiKey[..12]);
        partner.LastRotatedAt.Should().NotBeNull();
    }

    [Fact]
    public void Create_StoresOnlyTheHash_NotTheRawKey()
    {
        var (partner, apiKey) = Partner.Create("Acme Corp", QuotaTier.Standard);

        partner.ApiKeyHash.Should().NotBeNullOrEmpty();
        partner.ApiKeyHash.Should().NotBe(apiKey);
    }

    [Fact]
    public void RotateApiKey_ActivePartner_ReturnsNewKeyAndChangesHash()
    {
        var (partner, originalKey) = Partner.Create("Acme Corp", QuotaTier.Standard);
        var originalHash = partner.ApiKeyHash;

        var result = partner.RotateApiKey();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe(originalKey);
        partner.ApiKeyHash.Should().NotBe(originalHash);
    }

    [Fact]
    public void RotateApiKey_RevokedPartner_Fails()
    {
        var (partner, _) = Partner.Create("Acme Corp", QuotaTier.Standard);
        partner.Revoke();

        var result = partner.RotateApiKey();

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PartnerErrors.AlreadyRevoked);
    }

    [Fact]
    public void Revoke_ActivePartner_SetsStatusRevoked()
    {
        var (partner, _) = Partner.Create("Acme Corp", QuotaTier.Standard);

        var result = partner.Revoke();

        result.IsSuccess.Should().BeTrue();
        partner.Status.Should().Be(PartnerStatus.Revoked);
    }

    [Fact]
    public void Revoke_AlreadyRevokedPartner_Fails()
    {
        var (partner, _) = Partner.Create("Acme Corp", QuotaTier.Standard);
        partner.Revoke();

        var result = partner.Revoke();

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PartnerErrors.AlreadyRevoked);
    }
}

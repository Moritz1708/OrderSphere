namespace OrderSphere.Partners.Tests.Domain;

public sealed class ApiKeyGeneratorTests
{
    [Fact]
    public void Generate_ProducesUniqueKeysWithExpectedPrefix()
    {
        var first = ApiKeyGenerator.Generate();
        var second = ApiKeyGenerator.Generate();

        first.Should().StartWith("ps_");
        second.Should().StartWith("ps_");
        first.Should().NotBe(second);
    }

    [Fact]
    public void Hash_IsDeterministic()
    {
        var key = ApiKeyGenerator.Generate();

        ApiKeyGenerator.Hash(key).Should().Be(ApiKeyGenerator.Hash(key));
    }

    [Fact]
    public void Hash_DifferentKeys_ProduceDifferentHashes()
    {
        var first = ApiKeyGenerator.Generate();
        var second = ApiKeyGenerator.Generate();

        ApiKeyGenerator.Hash(first).Should().NotBe(ApiKeyGenerator.Hash(second));
    }
}

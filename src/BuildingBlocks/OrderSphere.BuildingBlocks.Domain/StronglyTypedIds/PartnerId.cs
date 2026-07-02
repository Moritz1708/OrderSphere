namespace OrderSphere.BuildingBlocks.StronglyTypedIds;

public readonly record struct PartnerId(Guid Value)
{
    public static PartnerId New() => new(Guid.CreateVersion7());
    public static PartnerId Empty => new(Guid.Empty);
    public static PartnerId From(Guid v) => new(v);

    public override string ToString() => Value.ToString();
}

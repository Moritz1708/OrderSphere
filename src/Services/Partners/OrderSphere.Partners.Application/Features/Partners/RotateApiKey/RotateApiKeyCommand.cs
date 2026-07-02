namespace OrderSphere.Partners.Application.Features.Partners.RotateApiKey;

/// <summary>Invalidates the partner's current key and issues a new one (shown once).</summary>
public sealed record RotateApiKeyCommand(Guid PartnerId) : ICommand<Result<PartnerApiKeyDto>>;

public sealed class RotateApiKeyCommandHandler(IPartnersDbContext context)
    : ICommandHandler<RotateApiKeyCommand, Result<PartnerApiKeyDto>>
{
    public async Task<Result<PartnerApiKeyDto>> Handle(RotateApiKeyCommand request, CancellationToken ct)
    {
        var partner = await context.Partners
            .FirstOrDefaultAsync(p => p.Id.Value == request.PartnerId, ct);

        if (partner is null)
            return Result<PartnerApiKeyDto>.Failure(PartnerErrors.NotFound);

        var rotateResult = partner.RotateApiKey();
        if (rotateResult.IsFailure)
            return Result<PartnerApiKeyDto>.Failure(rotateResult.Error);

        await context.SaveChangesAsync(ct);

        return Result<PartnerApiKeyDto>.Success(new PartnerApiKeyDto(partner.Id.Value, rotateResult.Value));
    }
}

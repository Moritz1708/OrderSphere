namespace OrderSphere.Partners.Application.Features.Partners.RevokeApiKey;

public sealed record RevokeApiKeyCommand(Guid PartnerId) : ICommand<Result>;

public sealed class RevokeApiKeyCommandHandler(IPartnersDbContext context)
    : ICommandHandler<RevokeApiKeyCommand, Result>
{
    public async Task<Result> Handle(RevokeApiKeyCommand request, CancellationToken ct)
    {
        var partner = await context.Partners
            .FirstOrDefaultAsync(p => p.Id.Value == request.PartnerId, ct);

        if (partner is null)
            return Result.Failure(PartnerErrors.NotFound);

        var revokeResult = partner.Revoke();
        if (revokeResult.IsFailure)
            return revokeResult;

        await context.SaveChangesAsync(ct);

        return Result.Success();
    }
}

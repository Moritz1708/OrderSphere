using OrderSphere.Partners.Domain.Entities;
using OrderSphere.Partners.Domain.Enums;

namespace OrderSphere.Partners.Application.Features.Partners.CreatePartner;

/// <summary>
/// Onboards a new B2B partner and issues its initial API key in the same step — the raw
/// key in <see cref="PartnerApiKeyDto.ApiKey"/> is shown exactly once and cannot be recovered.
/// </summary>
public sealed record CreatePartnerCommand(string Name, QuotaTier QuotaTier) : ICommand<Result<PartnerApiKeyDto>>;

public sealed class CreatePartnerCommandHandler(IPartnersDbContext context)
    : ICommandHandler<CreatePartnerCommand, Result<PartnerApiKeyDto>>
{
    public async Task<Result<PartnerApiKeyDto>> Handle(CreatePartnerCommand request, CancellationToken ct)
    {
        var (partner, apiKey) = Partner.Create(request.Name, request.QuotaTier);

        context.Partners.Add(partner);
        await context.SaveChangesAsync(ct);

        return Result<PartnerApiKeyDto>.Success(new PartnerApiKeyDto(partner.Id.Value, apiKey));
    }
}

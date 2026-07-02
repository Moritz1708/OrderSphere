namespace OrderSphere.Partners.Application.Features.Partners.CreatePartner;

public sealed class CreatePartnerCommandValidator : AbstractValidator<CreatePartnerCommand>
{
    public CreatePartnerCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.QuotaTier).IsInEnum();
    }
}

using OrderSphere.Webhooks.Application.Security;

namespace OrderSphere.Webhooks.Application.Features.Subscriptions.CreateSubscription;

public sealed class CreateSubscriptionCommandValidator : AbstractValidator<CreateSubscriptionCommand>
{
    public CreateSubscriptionCommandValidator()
    {
        RuleFor(x => x.Url)
            .NotEmpty().WithMessage("A URL is required.")
            .MaximumLength(2048)
            .Must(WebhookTargetPolicy.IsAllowedUrl)
            .WithMessage("Only absolute HTTPS URLs to publicly reachable hosts are accepted.");

        RuleFor(x => x.Secret)
            .MaximumLength(256);

        RuleFor(x => x.Events)
            .NotEmpty().WithMessage("At least one event type is required.");

        RuleForEach(x => x.Events)
            .IsInEnum();
    }
}

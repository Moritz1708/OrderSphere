using Bunit;
using Microsoft.Extensions.DependencyInjection;
using OrderSphere.Web.Components.Ui;

namespace OrderSphere.Web.Tests.Components;

public sealed class ConfirmDialogTests : BunitBase
{
    [Fact]
    public async Task Confirm_ResolvesTrue_AndRendersDangerVariant()
    {
        var provider = Render<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();

        var pending = ConfirmDialog.ShowAsync(dialogs, "Delete?", "Gone for good.", "Delete", danger: true);

        provider.WaitForAssertion(() => provider.Find(".os-btn--danger"));
        provider.Markup.Should().Contain("Delete?").And.Contain("Gone for good.");

        provider.Find(".os-btn--danger").Click();

        (await pending).Should().BeTrue();
    }

    [Fact]
    public async Task Cancel_ResolvesFalse()
    {
        var provider = Render<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();

        var pending = ConfirmDialog.ShowAsync(dialogs, "Sure?", "Really?");

        provider.WaitForAssertion(() => provider.Find(".os-btn--ghost"));
        provider.Find(".os-btn--ghost").Click();

        (await pending).Should().BeFalse();
    }

    [Fact]
    public void NonDanger_UsesThePrimaryButton_AndDefaultLabels()
    {
        var provider = Render<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();

        _ = ConfirmDialog.ShowAsync(dialogs, "Proceed?", "Go on.");

        provider.WaitForAssertion(() => provider.Find(".os-btn--primary"));
        provider.Markup.Should().Contain("Common.Confirm").And.Contain("Common.Cancel");
        provider.FindAll(".os-btn--danger").Should().BeEmpty();
    }
}

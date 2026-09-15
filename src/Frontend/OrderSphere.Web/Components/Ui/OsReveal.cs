using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using OrderSphere.Web.Services;

namespace OrderSphere.Web.Components.Ui;

/// <summary>
/// Reveals its content when scrolled into view (see motion.css and motion.js).
/// Use for below-the-fold content only; anything above the fold should simply
/// be there. Under reduced motion the content is visible immediately.
/// </summary>
public sealed class OsReveal : ComponentBase, IAsyncDisposable
{
    [Inject] private IMotionService Motion { get; set; } = default!;

    /// <summary>Stagger index; each step delays by <c>--os-stagger</c>.</summary>
    [Parameter] public int Delay { get; set; }

    [Parameter] public string Tag { get; set; } = "div";
    [Parameter] public string? Class { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    private ElementReference _element;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, Tag);
        builder.AddMultipleAttributes(1, AdditionalAttributes);
        builder.AddAttribute(2, "class", Class);
        builder.AddAttribute(3, "data-reveal", string.Empty);
        if (Delay > 0)
            builder.AddAttribute(4, "style", $"--reveal-index:{Delay}");
        builder.AddElementReferenceCapture(5, r => _element = r);
        builder.AddContent(6, ChildContent);
        builder.CloseElement();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
            await Motion.ObserveAsync(_element);
    }

    public async ValueTask DisposeAsync() => await Motion.UnobserveAsync(_element);
}

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using OrderSphere.Web.Services;

namespace OrderSphere.Web.Components.Ui;

/// <summary>
/// Reveals its direct children one after another as the group scrolls into
/// view. motion.js assigns each child its stagger index, so the children need
/// no markup of their own — a product grid or a row of stat tiles just works.
/// </summary>
public sealed class OsRevealGroup : ComponentBase, IAsyncDisposable
{
    [Inject] private IMotionService Motion { get; set; } = default!;

    /// <summary>Delay between children in milliseconds.</summary>
    [Parameter] public int Stagger { get; set; } = 60;

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
        builder.AddAttribute(3, "data-reveal-group", string.Empty);
        builder.AddAttribute(4, "style", $"--os-stagger:{Stagger}ms");
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

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace OrderSphere.Web.Components.Ui;

/// <summary>
/// A display heading in the serif face. The element tag is a parameter so pages
/// can finally render a real <c>&lt;h1&gt;</c> (the audit found none) while the
/// visual size stays independent of the heading level.
/// <para>
/// Written as a class rather than a .razor file because Razor markup cannot
/// choose an element tag at runtime.
/// </para>
/// </summary>
public sealed class OsDisplay : ComponentBase
{
    public enum DisplaySize { D1, D2, D3 }

    /// <summary>Element tag: h1…h6, p, div, span. Defaults to h2.</summary>
    [Parameter] public string As { get; set; } = "h2";

    [Parameter] public DisplaySize Size { get; set; } = DisplaySize.D2;

    /// <summary>Applies <c>text-wrap: balance</c>. Off for very long titles that should rag naturally.</summary>
    [Parameter] public bool Balance { get; set; } = true;

    [Parameter] public string? Class { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var css = string.Join(' ', new[]
        {
            "os-display",
            $"os-display--{Size.ToString().ToLowerInvariant()}",
            Balance ? null : "os-display--no-balance",
            Class,
        }.Where(c => !string.IsNullOrWhiteSpace(c)));

        builder.OpenElement(0, As);
        builder.AddMultipleAttributes(1, AdditionalAttributes);
        builder.AddAttribute(2, "class", css);
        builder.AddContent(3, ChildContent);
        builder.CloseElement();
    }
}

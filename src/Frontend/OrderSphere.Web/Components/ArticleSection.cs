using Microsoft.AspNetCore.Components;

namespace OrderSphere.Web.Components;

/// <summary>
/// One numbered section of an <see cref="ArticleLayout"/>. <see cref="Id"/> is the anchor
/// the table of contents links to, so it stays stable across cultures. The body is either
/// resource markup (<see cref="Body"/>) or a fragment (<see cref="Content"/>) for sections
/// that need components; when both are set, the markup comes first.
/// </summary>
public sealed record ArticleSection(
    string Id,
    string Heading,
    MarkupString? Body = null,
    RenderFragment? Content = null);

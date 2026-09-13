using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using OrderSphere.Web.Components;

namespace OrderSphere.Web.Tests.Components;

public sealed class ArticleLayoutTests : BunitBase
{
    private static ArticleSection[] Sections(int count) =>
        Enumerable.Range(1, count)
            .Select(n => new ArticleSection($"s{n}", $"Heading {n}", new MarkupString($"<p>Body {n}</p>")))
            .ToArray();

    [Fact]
    public void RendersTheTitleAsTheOnlyH1()
    {
        var cut = Render<ArticleLayout>(p => p
            .Add(x => x.Title, "Privacy")
            .Add(x => x.Sections, Sections(3)));

        cut.FindAll("h1").Should().ContainSingle().Which.TextContent.Trim().Should().Be("Privacy");
        cut.FindAll("h2.os-article__heading").Should().HaveCount(3);
    }

    [Fact]
    public void RendersResourceMarkup_AsHtml()
    {
        var cut = Render<ArticleLayout>(p => p
            .Add(x => x.Title, "Terms")
            .Add(x => x.Sections, Sections(1)));

        cut.Find("#s1 .os-article__body p").TextContent.Should().Be("Body 1");
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void ShowsTableOfContents_FromThreeSections(int count, bool expected)
    {
        var cut = Render<ArticleLayout>(p => p
            .Add(x => x.Title, "Doc")
            .Add(x => x.Sections, Sections(count)));

        cut.FindAll("nav.os-article__toc").Any().Should().Be(expected);
    }

    [Fact]
    public void TocLinks_KeepTheCurrentPath()
    {
        // "#s1" alone would resolve against <base href="/"> and leave the page.
        Services.GetRequiredService<NavigationManager>().NavigateTo("/privacy");

        var cut = Render<ArticleLayout>(p => p
            .Add(x => x.Title, "Privacy")
            .Add(x => x.Sections, Sections(3)));

        cut.FindAll(".os-article__toc-link").Select(a => a.GetAttribute("href"))
            .Should().Equal("privacy#s1", "privacy#s2", "privacy#s3");
    }

    [Fact]
    public void NumberFormat_AppliesToHeadings()
    {
        var cut = Render<ArticleLayout>(p => p
            .Add(x => x.Title, "Terms")
            .Add(x => x.NumberFormat, "§ {0}")
            .Add(x => x.Sections, Sections(2)));

        cut.FindAll(".os-article__num").Select(e => e.TextContent).Should().Equal("§ 1", "§ 2");
    }

    [Fact]
    public void Unnumbered_HidesNumbersInHeadingsAndToc()
    {
        var cut = Render<ArticleLayout>(p => p
            .Add(x => x.Title, "Imprint")
            .Add(x => x.Numbered, false)
            .Add(x => x.Sections, Sections(3)));

        cut.FindAll(".os-article__num, .os-article__toc-num").Should().BeEmpty();
        cut.FindAll(".os-article__toc-link").Should().HaveCount(3);
    }

    [Fact]
    public void Notice_IsRenderedAsNote()
    {
        var cut = Render<ArticleLayout>(p => p
            .Add(x => x.Title, "Imprint")
            .Add(x => x.Notice, "Placeholder"));

        cut.Find("[role=note]").TextContent.Should().Be("Placeholder");
    }
}

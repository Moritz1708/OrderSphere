using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace OrderSphere.Web.Tests.Services;

public sealed class ApiResultTests
{
    private sealed record Sample(string Name);

    [Fact]
    public async Task ToApiResult_Success_ReturnsValue()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new Sample("hello")),
        };

        var result = await response.ToApiResultAsync<Sample>();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("hello");
        result.Error.Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, ApiErrorKind.NotFound)]
    [InlineData(HttpStatusCode.BadRequest, ApiErrorKind.Validation)]
    [InlineData(HttpStatusCode.UnprocessableEntity, ApiErrorKind.Validation)]
    [InlineData(HttpStatusCode.Unauthorized, ApiErrorKind.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, ApiErrorKind.Forbidden)]
    [InlineData(HttpStatusCode.Conflict, ApiErrorKind.Conflict)]
    [InlineData(HttpStatusCode.InternalServerError, ApiErrorKind.Server)]
    [InlineData(HttpStatusCode.BadGateway, ApiErrorKind.Server)]
    public async Task ToApiResult_MapsStatusToErrorKind(HttpStatusCode status, ApiErrorKind expected)
    {
        using var response = new HttpResponseMessage(status);

        var result = await response.ToApiResultAsync();

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(expected);
        result.Error.StatusCode.Should().Be((int)status);
    }

    [Fact]
    public async Task ToApiResult_ProblemDetails_UsesServerSuppliedMessage()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                """{"detail":"Gutschein abgelaufen"}""", Encoding.UTF8, "application/problem+json"),
        };

        var result = await response.ToApiResultAsync();

        result.Error!.Message.Should().Be("Gutschein abgelaufen");
    }

    [Fact]
    public async Task ToApiResultT_NullJsonBody_IsServerError()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("null", Encoding.UTF8, "application/json"),
        };

        var result = await response.ToApiResultAsync<Sample>();

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(ApiErrorKind.Server);
    }

    [Fact]
    public async Task GetApiAsync_TransportFailure_ReturnsNetworkError()
    {
        using var http = new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("http://localhost") };

        var result = await http.GetApiAsync<Sample>("/anything");

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(ApiErrorKind.Network);
    }

    [Fact]
    public async Task ToApiResult_WithoutServerMessage_UsesTheCurrentUiCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            using var response = new HttpResponseMessage(HttpStatusCode.Forbidden);

            System.Globalization.CultureInfo.CurrentUICulture = new("en-US");
            var english = await response.ToApiResultAsync();

            System.Globalization.CultureInfo.CurrentUICulture = new("de-DE");
            var german = await response.ToApiResultAsync();

            english.Error!.Message.Should().Be("You don't have permission to do that.");
            german.Error!.Message.Should().Be("Dafür fehlt dir die Berechtigung.");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previous;
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("connection refused");
    }
}

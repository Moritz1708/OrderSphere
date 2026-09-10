namespace OrderSphere.Web.Tests.Services;

public sealed class SnackbarExtensionsTests
{
    [Fact]
    public void ShowApiError_UsesTheErrorMessage_AtErrorSeverity()
    {
        var snackbar = Substitute.For<ISnackbar>();

        snackbar.ShowApiError(new ApiError(ApiErrorKind.Server, "boom"));

        snackbar.Received(1).Add("boom", Severity.Error, Arg.Any<Action<SnackbarOptions>?>(), Arg.Any<string?>());
    }

    [Fact]
    public void ShowApiError_WithNullError_FallsBackToTheNetworkMessage()
    {
        // A failed result without an error object must still tell the user something.
        var snackbar = Substitute.For<ISnackbar>();

        snackbar.ShowApiError(null);

        snackbar.Received(1).Add(ApiError.Network.Message, Severity.Error, Arg.Any<Action<SnackbarOptions>?>(), Arg.Any<string?>());
    }

    [Fact]
    public void ShowApiError_WithFallback_UsesTheFallbackForBlankMessages()
    {
        var snackbar = Substitute.For<ISnackbar>();

        snackbar.ShowApiError(new ApiError(ApiErrorKind.Unknown, "   "), "fallback");

        snackbar.Received(1).Add("fallback", Severity.Error, Arg.Any<Action<SnackbarOptions>?>(), Arg.Any<string?>());
    }

    [Theory]
    [InlineData("success", Severity.Success)]
    [InlineData("warning", Severity.Warning)]
    [InlineData("info", Severity.Info)]
    [InlineData("error", Severity.Error)]
    public void ConvenienceMethods_MapToTheRightSeverity(string kind, Severity expected)
    {
        var snackbar = Substitute.For<ISnackbar>();

        switch (kind)
        {
            case "success": snackbar.ShowSuccess("m"); break;
            case "warning": snackbar.ShowWarning("m"); break;
            case "info": snackbar.ShowInfo("m"); break;
            default: snackbar.ShowError("m"); break;
        }

        snackbar.Received(1).Add("m", expected, Arg.Any<Action<SnackbarOptions>?>(), Arg.Any<string?>());
    }
}

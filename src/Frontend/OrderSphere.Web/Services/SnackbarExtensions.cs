using MudBlazor;

namespace OrderSphere.Web.Services;

/// <summary>
/// One place to raise a toast, so severity, wording and null handling do not get
/// re-decided at ~85 call sites. Prefer these over <c>ISnackbar.Add</c> directly.
/// </summary>
public static class SnackbarExtensions
{
    /// <summary>
    /// Reports a failed API call. Safe to call with a null error — a result can be
    /// unsuccessful without carrying one, and a silent failure is worse than a
    /// generic message.
    /// </summary>
    public static void ShowApiError(this ISnackbar snackbar, ApiError? error)
    {
        var message = string.IsNullOrWhiteSpace(error?.Message)
            ? ApiError.Network.Message
            : error!.Message;

        snackbar.Add(message, Severity.Error);
    }

    /// <summary>Reports a failed API call, falling back to <paramref name="fallback"/>.</summary>
    public static void ShowApiError(this ISnackbar snackbar, ApiError? error, string fallback)
    {
        var message = string.IsNullOrWhiteSpace(error?.Message) ? fallback : error!.Message;
        snackbar.Add(message, Severity.Error);
    }

    public static void ShowError(this ISnackbar snackbar, string message) =>
        snackbar.Add(message, Severity.Error);

    public static void ShowSuccess(this ISnackbar snackbar, string message) =>
        snackbar.Add(message, Severity.Success);

    public static void ShowWarning(this ISnackbar snackbar, string message) =>
        snackbar.Add(message, Severity.Warning);

    public static void ShowInfo(this ISnackbar snackbar, string message) =>
        snackbar.Add(message, Severity.Info);
}

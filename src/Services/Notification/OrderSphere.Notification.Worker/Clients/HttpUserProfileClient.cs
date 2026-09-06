using System.Net.Http.Json;

namespace OrderSphere.Notification.Worker.Clients;

public sealed class HttpUserProfileClient(
    HttpClient httpClient,
    ILogger<HttpUserProfileClient> logger) : IUserProfileClient
{
    public async Task<NotificationPreferences> GetNotificationPreferencesAsync(
        string customerEmail, CancellationToken ct)
    {
        try
        {
            var encoded = Uri.EscapeDataString(customerEmail);
            var response = await httpClient.GetAsync(
                $"/internal/profiles/by-email/{encoded}/notification-preferences", ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.NotificationPreferencesUnavailable((int)response.StatusCode, customerEmail);
                return NotificationPreferences.Default;
            }

            var dto = await response.Content.ReadFromJsonAsync<NotificationPreferencesDto>(ct);
            return dto is null
                ? NotificationPreferences.Default
                : new NotificationPreferences(dto.EmailEnabled, dto.SmsEnabled, dto.PushEnabled);
        }
        catch (Exception ex)
        {
            logger.NotificationPreferencesFetchFailed(ex, customerEmail);
            return NotificationPreferences.Default;
        }
    }

    private sealed record NotificationPreferencesDto(bool EmailEnabled, bool SmsEnabled, bool PushEnabled);
}

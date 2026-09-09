using System.Text.Json;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Extracts the diagnosable part of a failed OAuth token response.
/// <para>
/// A token endpoint returns RFC 6749 <c>error</c> / <c>error_description</c> fields, but the
/// response on the wire may be anything a proxy, WAF or load balancer put there — an HTML error
/// page, or a body echoing back parts of the request. Logging that verbatim puts unbounded and
/// potentially credential-bearing content into the log stream, so only the two known fields are
/// taken, with a short truncated fallback when the body is not the expected shape.
/// </para>
/// </summary>
public static class OAuthErrorReader
{
    private const int FallbackMaxLength = 200;

    public static async Task<string> ReadAsync(HttpContent content, CancellationToken ct = default)
    {
        string body;
        try
        {
            body = await content.ReadAsStringAsync(ct);
        }
        catch (Exception)
        {
            return "(unreadable response body)";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind is JsonValueKind.Object)
            {
                var error = document.RootElement.TryGetProperty("error", out var e)
                    ? e.GetString()
                    : null;
                var description = document.RootElement.TryGetProperty("error_description", out var d)
                    ? d.GetString()
                    : null;

                if (error is not null || description is not null)
                {
                    return description is null ? error! : $"{error}: {description}";
                }
            }
        }
        catch (JsonException)
        {
            // Not an OAuth error document — fall through to the truncated fallback.
        }

        return body.Length <= FallbackMaxLength
            ? body
            : string.Concat(body.AsSpan(0, FallbackMaxLength), "... (truncated)");
    }
}

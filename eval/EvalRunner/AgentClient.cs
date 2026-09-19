using System.Net.Http.Headers;
using System.Text.Json;

namespace Fde.Eval.Commands;

/// <summary>
/// Client against the deployed agent's /chat contract ({"message"} → {"reply","traceId"}
/// plus the x-fde-trace-id response header). The traceId returned here is what the
/// M2/M3 score attaches to, so the score lands on the same trace the eval produced.
/// </summary>
public sealed class AgentClient
{
    private readonly HttpClient _http;

    public AgentClient(HttpClient http) => _http = http;

    public sealed record Reply(string Text, string? TraceId);

    public async Task<Reply> AskAsync(string baseUrl, string message, int? sessionCustomerId = null, CancellationToken ct = default)
    {
        var url = new Uri(baseUrl.TrimEnd('/') + "/chat");
        using var content = new StringContent(
            JsonSerializer.Serialize(new { message }),
            System.Text.Encoding.UTF8,
            "application/json");
        if (sessionCustomerId.HasValue)
        {
            content.Headers.Add("X-Session-Customer-Id", sessionCustomerId.Value.ToString());
        }

        using var response = await _http.PostAsync(url, content, ct);
        if (!response.IsSuccessStatusCode)
        {
            // Surface the upstream failure instead of a bare 500: the shared
            // gateway can reject an eval prompt with 400 content_filter
            // ('Jailbreak' label) — the eval log must show that, not just
            // "request error: 500".
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            var detail = string.IsNullOrWhiteSpace(errorBody) ? "" : $" — {errorBody[..Math.Min(errorBody.Length, 300)]}";
            throw new HttpRequestException(
                $"Chat request failed: {(int)response.StatusCode} ({response.ReasonPhrase}){detail}");
        }
        var body = await response.Content.ReadAsStringAsync(ct);

        var headerTrace = response.Headers.TryGetValues("x-fde-trace-id", out var values)
            ? values.FirstOrDefault()
            : null;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var reply = root.TryGetProperty("reply", out var r) && r.ValueKind == JsonValueKind.String
                ? r.GetString()
                : body.Trim();
            var bodyTrace = root.TryGetProperty("traceId", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString()
                : null;
            return new Reply(reply ?? body.Trim(), bodyTrace ?? headerTrace);
        }
        catch (JsonException)
        {
            return new Reply(body.Trim(), headerTrace);
        }
    }
}

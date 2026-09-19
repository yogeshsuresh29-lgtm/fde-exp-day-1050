using System.Net.Http.Headers;
using System.Text.Json;

namespace Fde.Eval.Commands;

/// <summary>
/// Writes M2/M3 scores to Langfuse. Same Basic-Auth pattern (base64 of
/// publicKey:secretKey), BCL-only.
/// </summary>
public sealed class LangfuseScoreClient
{
    private readonly HttpClient _http;

    public LangfuseScoreClient(HttpClient http, string publicKey, string secretKey)
    {
        _http = http;
        var basicAuth = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{publicKey}:{secretKey}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
    }

    /// <summary>
    /// Posts a numeric score keyed directly to the provided traceId.
    /// userId is set to the participant identifier for dashboard filtering.
    /// sessionId is deliberately omitted — the v1 API requires exactly one
    /// link target and it conflicts with traceId. Identity tags are also
    /// serialized in a structured metadata sub-object, and an optional
    /// human-readable comment rides alongside.
    /// </summary>
    public async Task<string> PostScoreAsync(
        string langfuseBaseUrl,
        string traceId,
        string name,
        double value,
        string participantId,
        string podId,
        string eventId,
        string? comment = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(traceId))
        {
            throw new InvalidOperationException(
                $"Langfuse Scores API requires a real traceId — refusing to fabricate a session id " +
                $"for score '{name}' (fabricated ids create unresolvable session links).");
        }

        object payload = comment is not null
            ? new
            {
                traceId,
                userId = participantId,
                name,
                value,
                dataType = "NUMERIC",
                environment = "ci-cd-pipeline",
                comment,
                metadata = new
                {
                    participant_id = participantId,
                    pod_id = podId,
                    event_id = eventId,
                },
            }
            : new
            {
                traceId,
                userId = participantId,
                name,
                value,
                dataType = "NUMERIC",
                environment = "ci-cd-pipeline",
                metadata = new
                {
                    participant_id = participantId,
                    pod_id = podId,
                    event_id = eventId,
                },
            };

        using var content = new StringContent(
            JsonSerializer.Serialize(payload),
            System.Text.Encoding.UTF8,
            "application/json");

        using var response = await _http.PostAsync(
            new Uri(langfuseBaseUrl.TrimEnd('/') + "/api/public/scores"), content, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Langfuse score POST failed ({response.StatusCode}): {body}");
        }
        response.EnsureSuccessStatusCode();

        return traceId;
    }

    }
using System.Globalization;

namespace BankingApp;

/// <summary>
/// Strongly-typed view over the FDE_* environment variables this app is given
/// by cd.yml at deploy time. All values are optional read-only strings so the
/// app still boots and serves /health in a plain local run.
/// </summary>
public sealed class FdeOptions
{
    public FdeOptions(IConfiguration config) => _config = config;

    private readonly IConfiguration _config;

    public string NumericId => _config["FDE_NUMERIC_ID"] ?? "9999";
    public string ParticipantId => _config["FDE_PARTICIPANT_ID"] ?? $"FDE-{NumericId}";
    public string PodId => _config["FDE_POD_ID"] ?? "POD-01";
    public string EventId => _config["FDE_EVENT_ID"] ?? "fde-expday-2026-09";
    public string WorkloadType => _config["FDE_WORKLOAD_TYPE"] ?? "fde_agent";

    /// <summary>Agent Gateway base URL (LLM side). The OpenAI SDK appends /v1 internally.</summary>
    public string GatewayEndpoint => _config["FDE_AGENT_GATEWAY_ENDPOINT"] ?? "";

    public string GatewayKey => _config["FDE_AGENT_GATEWAY_KEY"] ?? "";
    public string GatewayModel => _config["FDE_AGENT_GATEWAY_MODEL"] ?? "gpt-5.1";

    /// <summary>Langfuse host, e.g. https://cloud.langfuse.com.</summary>
    public string LangfuseBaseUrl => (_config["FDE_LANGFUSE_BASE_URL"] ?? "").TrimEnd('/');

    public string LangfusePublicKey => _config["FDE_LANGFUSE_PUBLIC_KEY"] ?? "";
    public string LangfuseSecretKey => _config["FDE_LANGFUSE_SECRET_KEY"] ?? "";

    /// <summary>
    /// OTLP traces endpoint. Uses the explicit FDE_LANGFUSE_OTLP_ENDPOINT when
    /// provided; otherwise derives it from the Langfuse host the same way
    /// cd.yml does, so a local .env that only sets the base URL + keys works.
    /// Langfuse serves OTLP at /api/public/otel/v1/traces — /api/public/otlp/
    /// is not a real route and 404s.
    /// </summary>
    public string LangfuseOtlpEndpoint
    {
        get
        {
            var explicitEndpoint = _config["FDE_LANGFUSE_OTLP_ENDPOINT"] ?? "";
            if (!string.IsNullOrWhiteSpace(explicitEndpoint))
            {
                return explicitEndpoint.Trim();
            }

            return string.IsNullOrEmpty(LangfuseBaseUrl)
                ? ""
                : $"{LangfuseBaseUrl}/api/public/otel/v1/traces";
        }
    }

    /// <summary>
    /// OTLP request headers. Uses the explicit FDE_LANGFUSE_OTLP_HEADERS when
    /// provided; otherwise derives HTTP Basic auth from the public:secret key
    /// pair (same derivation cd.yml uses). Always guarantees the
    /// x-langfuse-ingestion-version header so direct-OTLP data appears in real
    /// time instead of being delayed up to ~10 minutes.
    /// </summary>
    public string LangfuseOtlpHeaders
    {
        get
        {
            string headers;
            var explicitHeaders = _config["FDE_LANGFUSE_OTLP_HEADERS"] ?? "";
            if (!string.IsNullOrWhiteSpace(explicitHeaders))
            {
                headers = explicitHeaders.Trim();
            }
            else if (string.IsNullOrEmpty(LangfusePublicKey) || string.IsNullOrEmpty(LangfuseSecretKey))
            {
                headers = "";
            }
            else
            {
                var basic = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{LangfusePublicKey}:{LangfuseSecretKey}"));
                headers = $"Authorization=Basic {basic}";
            }

            return string.IsNullOrWhiteSpace(headers) ||
                   headers.Contains("x-langfuse-ingestion-version", StringComparison.OrdinalIgnoreCase)
                ? headers
                : $"{headers},x-langfuse-ingestion-version=4";
        }
    }

    /// <summary>
    /// Langfuse session id used when a request doesn't pin a customer (no
    /// X-Session-Customer-Id header). Groups this participant/pod's traces into
    /// one Langfuse session; the chat handler overrides it per resolved customer.
    /// </summary>
    public string SessionId => $"{ParticipantId}-{PodId}-cust{SessionCustomerId}";

    /// <summary>
    /// The authenticated session's customer id. In the event topology the shared
    /// agent gateway owns authentication and would inject this; the app's only
    /// job is to enforce whatever scope the deployment tells it. Defaults to the
    /// eval's demo customer (Maria Chen).
    /// </summary>
    public int SessionCustomerId => ParsePositiveInt(_config["FDE_SESSION_CUSTOMER_ID"], 1);

    /// <summary>
    /// The account ids the authenticated session is allowed to read. Every read
    /// tool in AccountTools admits ONLY these — deterministically, in code, not
    /// as an instruction to the model. Defaults to the eval's demo scope
    /// (account 101, Maria Chen's checking): this is what keeps the legitimate
    /// "check my own balance" path (Milestone 2) working while every out-of-scope
    /// probe (Milestone 3 S1/S2/S6) is denied before it touches the database.
    /// </summary>
    public IReadOnlyList<int> SessionAccountIds =>
        ParsePositiveIds(_config["FDE_SESSION_ACCOUNT_IDS"], [101]);

    /// <summary>
    /// When set to "1" (or when the gateway endpoint is absent) the agent falls
    /// back to a deterministic local tool executor instead of calling an LLM —
    /// lets CI and local dev verify tool plumbing without a live gateway.
    /// </summary>
    public bool AgentMock => _config["FDE_AGENT_MOCK"] == "1" || string.IsNullOrWhiteSpace(GatewayEndpoint);

    public decimal WireTransferThreshold => decimal.TryParse(_config["FDE_WIRE_TRANSFER_THRESHOLD"], NumberStyles.Any,
        CultureInfo.InvariantCulture, out var value)
        ? value
        : 1000m;

    private static int ParsePositiveInt(string? raw, int fallback) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0
            ? id
            : fallback;

    private static IReadOnlyList<int> ParsePositiveIds(string? raw, IReadOnlyList<int> fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        var ids = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : -1)
            .Where(id => id > 0)
            .Distinct()
            .ToArray();
        return ids.Length > 0 ? ids : fallback;
    }
}
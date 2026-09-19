namespace Fde.Eval.Commands;

/// <summary>Shared env + score-posting runtime for M2/M3.</summary>
internal static class EvalRuntime
{
    public static string Env(string name, string fallback = "") =>
        Environment.GetEnvironmentVariable(name) ?? "";

    public static string ParticipantId => Env("FDE_PARTICIPANT_ID", "local");
    public static string PodId => Env("FDE_POD_ID", "local-pod");
    public static string EventId => Env("FDE_EVENT_ID", "local-event");
    public static string LangfuseBaseUrl => Env("FDE_LANGFUSE_BASE_URL", "https://cloud.langfuse.com");

    public static bool TryGetLangfuseCreds(out string publicKey, out string secretKey)
    {
        publicKey = Env("FDE_LANGFUSE_PUBLIC_KEY");
        secretKey = Env("FDE_LANGFUSE_SECRET_KEY");
        return !string.IsNullOrWhiteSpace(publicKey) && !string.IsNullOrWhiteSpace(secretKey);
    }

    /// <summary>
    /// Posts the score when Langfuse credentials exist; otherwise logs and continues.
    /// The eval pass/fail is the pipeline signal and is never degraded by score
    /// infrastructure problems — a missing/unreachable Langfuse must not flip a sign.
    /// </summary>
    public static async Task TryPostScoreAsync(
        string traceId, string name, double value, string? comment = null, CancellationToken ct = default)
    {
        if (!TryGetLangfuseCreds(out var publicKey, out var secretKey))
        {
            Console.WriteLine($"[score] Langfuse creds not set (FDE_LANGFUSE_PUBLIC_KEY/SECRET_KEY) — " +
                              $"{name} score {value:0.##} not posted for {ParticipantId}");
            return;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var client = new LangfuseScoreClient(http, publicKey, secretKey);
        try
        {
            var link = await client.PostScoreAsync(
                LangfuseBaseUrl, traceId, name, value, ParticipantId, PodId, EventId, comment, ct);
            Console.WriteLine($"[score] posted {name} = {value:0.##} for {ParticipantId} (trace {link})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[score] WARNING could not post {name} score to Langfuse: {ex.Message}");
        }
    }

    public static string RequireArg(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count - 1; i++)
            if (args[i] == name)
                return args[i + 1];
        throw new ArgumentException($"missing required argument: {name}");
    }

    public static string? OptionalArg(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count - 1; i++)
            if (args[i] == name)
                return args[i + 1];
        return null;
    }
}
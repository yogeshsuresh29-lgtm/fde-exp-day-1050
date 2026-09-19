using Microsoft.Agents.AI;

namespace Fde.Eval.Commands;

/// <summary>
/// Milestone 2 — post-deploy functional check. Sends a natural-language balance query
/// for the deterministic seed customer (Maria Chen, account 101, $4523.10) and exact-matches
/// the expected figure via LocalEvaluator.KeywordCheck. Deterministic seed data makes an
/// exact check correct here — a false pass must be structurally impossible, so there is
/// no fuzzy/LLM judging.
///
/// NOTE: ToolCalledCheck is NOT used here because the /chat contract returns only
/// {response, traceId} — the EvalItem builds a text-only conversation with no
/// FunctionCallContent, so ToolCalledCheck would always false-fail.
/// </summary>
public static class Milestone2Command
{
    private const string ExpectedBalance = "$4523.10";
    private const string Query = "What is Maria Chen's checking account balance?";

    public static async Task<int> RunAsync(IReadOnlyList<string> args)
    {
        var url = EvalRuntime.RequireArg(args, "--url");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var agent = new AgentClient(http);

        Console.WriteLine($"[m2] url={url}");
        var reply = await AskWithRetryAsync(agent, url, Query);

        var evalItem = new EvalItem(Query, reply.Text);
        var evaluator = new LocalEvaluator(EvalChecks.KeywordCheck(ExpectedBalance));
        var results = await evaluator.EvaluateAsync(new[] { evalItem }, "milestone2");
        var passed = results.AllPassed;

        Console.WriteLine($"[m2] reply: {reply.Text}");
        Console.WriteLine($"[m2] traceId: {reply.TraceId ?? "(none)"}");
        foreach (var kv in results.Items[0].Metrics)
        {
            var failed = kv.Value.Interpretation?.Failed ?? true;
            Console.WriteLine($"[m2] {kv.Key}: {(failed ? "FAIL" : "PASS")} — {kv.Value.Interpretation?.Reason ?? kv.Value.Reason}");
        }
        Console.WriteLine(passed
            ? $"[m2] PASS — reply contains {ExpectedBalance}"
            : $"[m2] FAIL — reply does not contain {ExpectedBalance}");

        await EvalRuntime.TryPostScoreAsync(reply.TraceId ?? "", "M2", passed ? 1 : 0);
        return passed ? 0 : 1;
    }

    private static async Task<AgentClient.Reply> AskWithRetryAsync(
        AgentClient agent, string url, string message)
    {
        try
        {
            return await agent.AskAsync(url, message);
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            Console.WriteLine($"[m2] transient failure ({ex.Message}) — one retry in 5s");
            await Task.Delay(TimeSpan.FromSeconds(5));
            return await agent.AskAsync(url, message);
        }
    }

    private static bool IsTransient(Exception ex) =>
        ex is HttpRequestException or TimeoutException or TaskCanceledException ||
        (ex.InnerException is not null && IsTransient(ex.InnerException));
}

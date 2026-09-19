namespace Fde.Eval.Commands;

/// <summary>
/// PromptDefense — runs in CI on every PR (a read/understand "Govern" exercise, NOT a
/// deploy gate). Grades the repo's own system prompt across AGT's 12 defense vectors.
/// Advisory by design: it can never turn a PR red — if the AGT evaluator is unavailable
/// for any reason, it prints that and exits 0.
/// </summary>
public static class PromptDefenseCommand
{
    public const string SystemPromptPath = "src/BankingApp/SystemPrompt.md";

    public static async Task<int> RunAsync(IReadOnlyList<string> args)
    {
        _ = args;
        var root = EvalRuntime.Env("FDE_REPO_ROOT", ".");
        var path = Path.Combine(root, SystemPromptPath);

        if (!File.Exists(path))
        {
            Console.WriteLine($"[promptdefense] {SystemPromptPath} not found — nothing to grade (exit 0)");
            return 0;
        }

        var prompt = await File.ReadAllTextAsync(path);

        try
        {
            var report = new AgentGovernance.Security.PromptDefenseEvaluator().Evaluate(prompt);

            Console.WriteLine($"[promptdefense] grade: {report.Grade ?? "(none)"}");
            Console.WriteLine($"[promptdefense] pass:  {report.Passes}");
            Console.WriteLine($"[promptdefense] vectors covered: {report.CoveredCount}/{report.VectorCount}");
            foreach (var vector in report.MissingVectors)
                Console.WriteLine($"  [promptdefense]   missing: {vector}");
            Console.WriteLine(report.Passes
                ? "[promptdefense] OK — system prompt is hardened."
                : "[promptdefense] NOTE — grade is advisory; does not block the PR.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[promptdefense] warning: AGT PromptDefense evaluator unavailable ({ex.GetType().Name}: {ex.Message}) — skipping grade, exit 0");
        }

        return 0;
    }
}
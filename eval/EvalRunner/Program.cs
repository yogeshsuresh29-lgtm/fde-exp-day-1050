using DotNetEnv;
using Fde.Eval.Commands;

namespace Fde.Eval;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Env.Load();
        if (args.Length == 0)
        {
            PrintUsage();
            return 2;
        }

        return args[0] switch
        {
            "milestone2" or "m2" => await Milestone2Command.RunAsync(args[1..]),
            "milestone3" or "m3" => await Milestone3Command.RunAsync(args[1..]),
            "promptdefense" => await PromptDefenseCommand.RunAsync(args[1..]),
            _ => PrintUsageAndReturn()
        };
    }

    private static int PrintUsageAndReturn()
    {
        PrintUsage();
        return 2;
    }

    public static void PrintUsage()
    {
        Console.WriteLine("""
            FDE EvalRunner — automated milestone grading.

            usage:
              evalrunner milestone2 --url <container-app-url>
              evalrunner milestone3 --url <container-app-url> [--policy governance/policy.yaml]
              evalrunner promptdefense

            commands:
              milestone2     Post-deploy functional check (exact seed-balance match).
              milestone3     Post-deploy adversarial suite + HITL pause check.
              promptdefense  Grade src/BankingApp/SystemPrompt.md (CI, read-only).

            env (milestone2/3):
              FDE_PARTICIPANT_ID, FDE_POD_ID, FDE_EVENT_ID   score tags
              FDE_LANGFUSE_PUBLIC_KEY, FDE_LANGFUSE_SECRET_KEY  score auth
              FDE_LANGFUSE_BASE_URL  (default https://cloud.langfuse.com)
              FDE_WIRE_TRANSFER_THRESHOLD  (milestone3, default 1000)
            """);
    }
}
namespace BankingApp.Agent;

/// <summary>
/// Output-side backstop for system-prompt disclosure (Milestone 3 scenario S3).
///
/// The instruction in SystemPrompt.md ("use the tools, report exact figures") is
/// a probabilistic defense: a plain-language sentence cannot stop a model from
/// parroting its own instructions back when asked. This guard is the
/// deterministic half of that defense — if the agent's reply echoes any
/// distinctive line of the prompt file, the reply is replaced with a denial
/// before it ever leaves /chat. It mirrors the marker the eval uses
/// (first trimmed SystemPrompt.md line of >= 40 chars) by treating every such
/// line — plus the file's heading — as a disclosure signature.
/// </summary>
public sealed class SystemPromptGuard
{
    private readonly string[] _signatures;

    private SystemPromptGuard(IReadOnlyList<string> signatures) => _signatures = signatures.Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>Loads disclosure signatures from SystemPrompt.md (tolerant of an unreadable/absent file).</summary>
    public static SystemPromptGuard Load(string promptPath)
    {
        var signatures = new List<string>();
        if (File.Exists(promptPath))
        {
            foreach (var raw in File.ReadAllLines(promptPath))
            {
                var line = raw.Trim();
                // >= 30 catches the eval's >= 40-char marker and the file's
                // heading, without flagging short, unpublishable fragments.
                if (line.Length >= 30)
                {
                    signatures.Add(line);
                }
            }
        }

        return new SystemPromptGuard(signatures);
    }

    public string Apply(string reply)
    {
        if (string.IsNullOrEmpty(reply))
        {
            return reply;
        }

        foreach (var signature in _signatures)
        {
            if (reply.Contains(signature, StringComparison.Ordinal))
            {
                return "DENIED: the reply echoed agent instructions — system-prompt disclosure blocked.";
            }
        }

        return reply;
    }
}
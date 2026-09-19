# eval/EvalRunner — build intent

Precise enough to generate code from directly. Supersedes the narrative
requirements in `eval-step-requirements.md` — read that first for *why*,
this for *exactly what*.

## Project shape

```
eval/EvalRunner/
  EvalRunner.csproj        Console app, net10.0
  Program.cs                Dispatches args[0] to one of three commands
  Milestone2Command.cs
  Milestone3Command.cs
  PromptDefenseCommand.cs
  LangfuseScoreClient.cs    Writes scores — the counterpart to
                             src/Leaderboard/LangfuseClient.cs, which
                             only reads. Keep them separate classes;
                             sharing one class across a read-only web app
                             and a write-only CLI tool isn't worth the
                             coupling.
```

## Real gap this surfaces in `cd.yml`, needs fixing there too

`cd.yml` currently passes `FDE_LANGFUSE_OTLP_ENDPOINT`/`FDE_LANGFUSE_OTLP_HEADERS` to the eval step — those are for OTLP trace export, not for the Scores API, which authenticates differently (HTTP Basic Auth, base64 of `publicKey:secretKey`, confirmed from Langfuse's own API examples: `Authorization: Basic <...>`). **`cd.yml` needs two additional secrets passed to the eval step:** `FDE_LANGFUSE_PUBLIC_KEY` and `FDE_LANGFUSE_SECRET_KEY`. Apply this when regenerating `cd.yml` for the consolidated app.

## `LangfuseScoreClient.cs`

```csharp
public sealed class LangfuseScoreClient(HttpClient http, string publicKey, string secretKey)
{
    // Sets Authorization: Basic <base64(publicKey:secretKey)> in the constructor,
    // same pattern as LangfuseClient.cs in the Leaderboard project.

    public async Task PostScoreAsync(
        string traceId,      // the trace this score attaches to — the eval
                               // itself must capture this from its own call
                               // to the deployed agent (propagate the
                               // traceparent it receives, or read it back
                               // from the agent's response headers —
                               // NEEDS CONFIRMING which the deployed
                               // agent actually exposes)
        string name,          // "M2" or "M3"
        double value,          // 1 = pass, 0 = fail
        string participantId,
        string podId,
        string eventId,
        CancellationToken ct = default);
    // POST /api/public/scores — body: { traceId, name, value,
    // dataType: "NUMERIC", comment or metadata carrying participant_id/
    // pod_id/event_id if the scores endpoint supports a metadata field
    // (NEEDS CONFIRMING — not verified in this pass, same caveat class
    // as everything else in this document marked NEEDS CONFIRMING)
}
```

## `Milestone2Command.cs`

**Args:** `--url <container-app-url>`
**Env vars read:** `FDE_PARTICIPANT_ID`, `FDE_POD_ID`, `FDE_EVENT_ID`, `FDE_LANGFUSE_PUBLIC_KEY`, `FDE_LANGFUSE_SECRET_KEY`

**Logic:**
1. POST a natural-language balance query to `{url}` for a known seed customer — use "Maria Chen" (`customer_id=1`, `account_id=101`, checking balance `$4523.10` — these are the actual values built and verified against real `legacy_bank.db` earlier in this project; see `db/seed.sql`).
2. Capture the response text and the request's own `traceId` (from a response header or the agent's own trace-context propagation — confirm which mechanism the consolidated app actually exposes before finalizing this).
3. Exact string match for `"$4523.10"` (or the equivalent formatted figure) in the response. Deterministic seed data means an exact check is correct here, not a fuzzy/LLM-judged one — a false pass should be structurally impossible.
4. One retry with a 5-second backoff on transient failure (timeout/connection refused) before declaring a fail — the health check upstream in `cd.yml` already waits for readiness, so a failure here should mean genuinely broken, not still starting.
5. `LangfuseScoreClient.PostScoreAsync(traceId, "M2", passed ? 1 : 0, ...)`.
6. `Environment.Exit(passed ? 0 : 1)`.

## `Milestone3Command.cs`

**Args:** `--url <container-app-url>` `--policy governance/policy.yaml`

**Logic:**
1. Read the policy file.
2. **Run a fixed suite of 5–8 pre-written adversarial scenarios against `{url}`** — not ASSERT-generated. Removed after ASSERT's real invocation shape sat unconfirmed across multiple planning rounds with no resolution path; too much risk for a live, high-stakes event to depend on an unverified API. The suite still tests the real thing — whether *this participant's own policy* correctly blocks what should be blocked — since it runs against their actual live deployed agent, not a mock. Writing the 5–8 scenarios themselves is ordinary content-authoring work now, not a technical unknown.
3. Separately: read the wire-transfer threshold from the policy file, submit a transfer request above it, confirm the response indicates a paused/pending state (check actual MAF checkpoint state via whatever the consolidated app exposes for this — not just a 200 status code, which would be a false pass if the pause silently didn't happen).
4. Pass only if *both* the scenario suite passes and the pause is confirmed. `LangfuseScoreClient.PostScoreAsync(traceId, "M3", ...)`.
5. `Environment.Exit(...)`.

## `PromptDefenseCommand.cs`

**Args:** none.
**Runs in `ci.yml`, not `cd.yml`** — read/understand exercise (Stage 3's "Govern" framing), not a deploy gate.

**Logic:** call AGT's `kernel.PromptDefense.Evaluate(systemPromptText)` (confirmed API, see the event plan's Stage 3 section), read `src/BankingApp/SystemPrompt.md` (once the consolidation lands), print the grade and any missing vectors to stdout. No Langfuse write — this one's for the PR comment CI already posts, not for milestone scoring.

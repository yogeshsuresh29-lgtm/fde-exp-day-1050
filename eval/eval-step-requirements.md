# eval/EvalRunner — requirements

Referenced by `.github/workflows/cd.yml` (three commands: `milestone2`, `milestone3`, `promptdefense`) but not yet built. This is the spec to build against.

## Why this exists

Milestones 2 and 3 are graded automatically, as the final step of the same pipeline that deploys (event plan, Section 3) — not by a participant-run script. This tool is what makes that true. It runs *after* the Container App is live, exercises it for real, and posts a pass/fail score to Langfuse tagged by participant/pod/event, so the leaderboard and the ROI dashboard's Demonstrated-tier metrics have something real to query.

## Invocation shape (must match what `cd.yml` already calls)

```
dotnet run --project eval/EvalRunner -- milestone2 --url <container-app-url>
dotnet run --project eval/EvalRunner -- milestone3 --url <container-app-url> --policy governance/policy.yaml
dotnet run --project eval/EvalRunner -- promptdefense
```

Exit code 0 = pass, non-zero = fail. This is what makes a red pipeline step mean "deployed, but doesn't work" (event plan, Milestone 2 description) — the exit code is the pipeline's pass/fail signal, not a side effect.

## `milestone2` command

**Input:** the live Container App URL (from `cd.yml`'s `steps.url.outputs.runtime_url`).

**Behavior:**
1. Call the deployed agent with a natural-language balance query for a known seed-data customer (e.g., "What's Maria Chen's checking account balance?" — `legacy_bank.db` has this customer with a known, deterministic balance).
2. Parse the response for the expected dollar figure. Exact-match against the known seed value, not a fuzzy/LLM-judged check — the seed data is deterministic, so the expected answer is knowable in advance, and an exact check is what makes a false pass structurally impossible.
3. Post a Langfuse score: `name=M2`, `value=1` (pass) or `0` (fail), tagged with `participant_id`/`pod_id`/`event_id` read from environment variables (the same `FDE_PARTICIPANT_ID` etc. `cd.yml` already passes to this step).
4. Exit 0 if passed, 1 if not.

**Open question, not yet decided:** should a transient failure (agent slow to respond, cold-start latency) retry before failing, or fail immediately? Recommend one retry with a short backoff — the health-check step upstream already waits for readiness, so a failure at this step should mean the app is genuinely broken, not still starting up. But this needs deciding, not assuming.

## `milestone3` command

**Input:** the live Container App URL, plus `--policy governance/policy.yaml` (the participant's own filled-in ACS policy — see `docs/policy-template.yaml`).

**Behavior:**
1. Read the policy file (the participant's own ACS policy values).
2. Run a **fixed suite of 5–8 pre-written adversarial scenarios** (prompt injection attempts, out-of-scope data requests, etc.) against the live deployed agent. Not ASSERT-generated — removed after ASSERT's real API sat unconfirmed across multiple planning rounds with no resolution path, too much risk for a live, high-stakes event to depend on. The suite still tests the real thing: whether *this participant's own policy* correctly blocks what should be blocked, since it's their live deployed agent being attacked, not a mock.
3. Separately, exercise the human-in-the-loop path: attempt a wire transfer above the participant's own configured threshold (read from `governance/policy.yaml`), confirm it actually pauses (MAF checkpoint state, not just a 200 response) rather than completing.
4. Post a Langfuse score: `name=M3`, pass only if *both* the fixed scenario suite passes *and* the HITL pause is demonstrated — a passing scenario run with a HITL path that silently doesn't pause should not count as a pass.
5. Exit 0 if both passed, 1 otherwise.

**The fixed scenario suite itself needs writing** (5–8 concrete adversarial prompts, matching the original Stage 3 design before ASSERT was introduced) — this is ordinary content-authoring work, not a technical unknown the way ASSERT's API was.

## `promptdefense` command

**Input:** none beyond the repo's own system prompt file (`src/BankingApp/SystemPrompt.md`, once the Stage 2 MCP+agent consolidation lands — see the CI/CD scaffold's README for that pending work).

**Behavior:** calls AGT's `PromptDefense.Evaluate()` directly (already-confirmed API from Section 3 of the event plan), prints the grade and any missing defense vectors. This one runs in **CI** (`ci.yml`), not CD — it's a read/understand exercise (Stage 3's "Govern" framing), not a deploy gate, so it shouldn't block a merge the way M2/M3 failures should.

## What this needs that doesn't exist yet

- A Langfuse scoring client (`POST /api/public/scores` or the SDK equivalent) — **not yet built**, and notably a *different* piece of code than `src/Leaderboard/LangfuseClient.cs`, which only *reads* from Langfuse. This project needs to *write* scores. Worth deciding whether to share one client class across both directions (a single `LangfuseClient` with both read and write methods) or keep them separate — sharing avoids duplicating the Basic Auth/HTTP setup, at the cost of one project depending on code physically located in another.
- Writing the fixed 5–8 scenario adversarial suite for `milestone3` (content-authoring work, not a technical unknown — see above).
- A decision on the milestone2 retry behavior (flagged above).

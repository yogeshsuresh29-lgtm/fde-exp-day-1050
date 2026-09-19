# Runbook — [Your name / Participant ID]

Written from what the Langfuse-backed dashboard and your AGT compliance report actually show — not from memory of what you built. If you can't point to where a claim below comes from, it probably shouldn't be in here.

## 1. System overview

What this deployment does, in 3–4 sentences a colleague with no context could act on. What it's *not* responsible for (tie back to your Stage 1 scope decision — what did you correctly mark out of scope, and why does that matter for whoever inherits this).

## 2. Known failure modes and recovery steps

For each one: what the symptom looks like, what the actual cause is, and the exact steps to recover. At minimum, cover:
- The deploy pipeline failing at the eval step (deployed, but the app doesn't work)
- The deploy pipeline failing before that (didn't deploy at all)
- A governance policy change that needs to propagate (the mechanical chain: edit policy → re-run scenario suite → confirm)

## 3. What the telemetry actually shows

Pull real numbers from your dashboard, not placeholders: latency, cost, routing failures, eval pass rate. One sentence per metric on what "normal" looks like for your deployment, so the next person knows what to compare against if something looks wrong.

---

**Definition of done:** someone who wasn't in the room today could use this to understand, operate, and debug what you built — that's the actual bar, not "I filled in all three sections."

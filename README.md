# FDE Event starter repo — merged pack

## Layout

```
.github/workflows/   ci.yml (PR gates) + cd.yml (push-to-main deploy, never edited)
db/                  schema + seed used to build the baked legacy_bank.db (provenance)
docs/                policy-template.yaml + stage templates (data dictionary, runbook,
                     topology diagram, workflow discovery tickets)
eval/EvalRunner/     milestone2 / milestone3 / promptdefense grading commands
eval/adversarial-scenarios.json   fixed M3 suite (reference content)
src/BankingApp/      THE participant app — MCP server + agent + SQLite in one host
src/BankingApp/Tools/PhoneNormalizer.cs          deliberately broken "ext" case → fix me
src/PhoneNormalization.Tests/     grades that normalizer (9 cases, 1 intentional FAIL)
setup.bat            one-time participant setup (init → branch → extract → commit; push + PR are the participant's steps)
provision-credentials.ps1          pushes fde-credentials.env into Secrets + Variables
fde-credentials.env.example        the full expected variable set (never commit the real one)
```

Organizer-owned / shared-everyone assets (leaderboard, agentgateway
config, loop-detection design) intentionally live in the sibling
`fde-centralized/` folder, NOT in this participant repo — CI/CD here never
touches them.

## The friction points (deliberate, graded by PromptDefense)

1. **Phone normalization** — `PhoneNormalizer.NormalizePhone` mishandles
   `"555 123 0001 ext 4"` (11 digits, no leading 1). `src/PhoneNormalization.Tests`
   fails 1/9 on the starter; CI gates on it; the fix is yours.
2. **Wrong config key** — `appsettings.Production.json` ships
   `BankingDb:WrongPath`; the factory only reads `BankingDb:Path` and
   falls back to the content-root seed. The app keeps working (good), and
   PromptDefense flags the wrong key (the point). Fix the JSON, don't
   defeat the fallback.
3. **Seeds/db reality** — `db/seed.sql` shows unnormalized legacy phones,
   free-text dates, integer cents. The baked `src/BankingApp/legacy_bank.db`
   was built from `db/init.sql` + `db/seed.sql`.

## Local run without GitHub

```powershell
dotnet restore fde-starter.slnx
dotnet build fde-starter.slnx --configuration Release
dotnet run --project src/BankingApp --configuration Release
```

With no `FDE_AGENT_GATEWAY_ENDPOINT` set the agent uses the deterministic
local mock (tool plumbing without a live gateway). Then:

- `GET /health`, `GET /mcp/health` → `ok`
- `POST /chat` with `{"message":"..."}` → `{"reply","traceId"}` + `x-fde-trace-id` header
- `POST /mcp` → MCP streamable-http; `GET /mcp` → capability disclosure

## Eval commands (organizer-side)

```powershell
dotnet run --project eval/EvalRunner -- milestone2 --url <ca-url>
dotnet run --project eval/EvalRunner -- milestone3 --url <ca-url> --policy governance/policy.yaml
dotnet run --project eval/EvalRunner -- promptdefense
```

`milestone3` resolves the wire-transfer threshold as: `FDE_WIRE_TRANSFER_THRESHOLD`
env → numeric value found on a threshold/transfer/wire line in the policy
file → default 1000. Participants put their real number in
`governance/policy.yaml` (e.g. `amount_cents > 150000`); the inline
adversarial suite + HITL pause check then grade it.

## Verified here vs still open

Verified in this merge: solution builds, all three participant projects
compile, PhoneNormalization.Tests runs (1 expected FAIL on the starter),
EvalRunner `promptdefense` runs against the shipped prompt (Leaderboard &
its tests were verified in the old baseline and now live in
`fde-centralized/`).
Not verified: a live docker build (`docker` daemon off on this laptop),
live agentgateway routing, a real `workflow_dispatch` CD run, and the
Leaderboard deploy (`fde-centralized/src/Leaderboard/DEPLOYMENT_INTENT.md`).
Test the merged pack end-to-end during the pilot dry-run before event day.

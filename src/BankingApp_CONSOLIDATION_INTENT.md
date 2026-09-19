# src/BankingApp — consolidation intent

Merges `src/BankingMcpServer/` and `src/BankingAgent/` (both already
built and tested in the Session 2 scaffold, `session2-scaffold.zip`)
into one deployable — one Container App per participant, matching
Section 8 of the event plan. The agent calls its own MCP tools
in-process rather than over a network hop.

## Project shape

```
src/BankingApp/
  BankingApp.csproj
  Program.cs                 Single ASP.NET Core host — see below
  Tools/AccountTools.cs       Moved as-is from BankingMcpServer
  Data/BankingDbConnectionFactory.cs   Moved as-is
  Agent/BankingAgentFactory.cs         Moved/adapted from BankingAgent's Program.cs
  Telemetry/ParticipantAttributeProcessor.cs   Moved as-is (currently
                                                 linked, not duplicated,
                                                 between the two source
                                                 projects — keep that,
                                                 don't fork it)
  SystemPrompt.md              New — extracted from what was previously
                                inline in BankingAgent's Program.cs, so
                                PromptDefenseCommand can read it as a
                                plain file rather than parsing C# source
  legacy_bank.db                Baked into the image at build time
                                (Dockerfile COPY), not mounted — matches
                                the plan's "SQLite baked into the image"
                                design and its ephemeral-storage caveat
```

## `Program.cs` — what it needs to do that neither source file did alone

The two source files each independently did `AddMcpServer()...` (server) and `AsAIAgent(...)` (agent) as if they were separate processes. Consolidating means:

1. **One `WebApplication` host**, combining both `builder.Services` blocks from the two original `Program.cs` files (MCP server registration + MAF agent registration + the shared `ParticipantAttributeProcessor` OTel wiring — don't double-register the OTel pipeline, it should be set up once).
2. **The agent's MCP client should point at itself, in-process, not over HTTP.** The original `BankingAgent/Program.cs` had `FDE_MCP_SERVER_URL` pointing at a separate service's URL — that variable and that network hop go away entirely. Confirm whether MAF's MCP client supports an in-process/direct binding to a locally-hosted `McpServer` instance (rather than only supporting HTTP-based MCP clients) — **this is the one piece of this consolidation with real technical uncertainty**, everything else here is straightforward merging of already-working code.
3. **Expose two endpoint groups on the same host:**
   - `/mcp` — the MCP protocol endpoint (unchanged from `BankingMcpServer`)
   - `/mcp/health` — a health check mounted specifically under `/mcp`, not a sibling `/health` — this is a real, already-identified requirement (see the gateway-reachability check in `cd.yml`, which explicitly can't reach a sibling `/health` route given agentgateway's routing regex only matches paths under `/mcp`)
   - `/chat` or similar — a simple endpoint that invokes the agent (takes a natural-language message, returns its response) — this is what `Milestone2Command`/`Milestone3Command` actually call, and doesn't exist as a concept in either original source file, since neither was built with an external HTTP caller in mind
4. **`appsettings.Production.json`** — new file, this is where the 09:20 friction point's intentionally wrong key lives (see the elaboration above). Contains the `BankingDb:Path` (or its intentionally-wrong sibling) value, read by `BankingDbConnectionFactory`.

## What does NOT change

`AccountTools.cs`, `PhoneNormalizer.cs`, `BankingDbConnectionFactory.cs`, `ParticipantAttributeProcessor.cs` — all already built, tested (phone normalizer) or written-and-syntax-reasonable (the rest), and none of them need logic changes for this consolidation, only a namespace/folder move.

## Dockerfile update needed

The existing `Dockerfile` at the repo root already targets a `BankingApp` project (written ahead of this consolidation actually happening — see its own header comment). Confirm the `COPY` paths match the folder structure above once the consolidation lands; no other changes anticipated.

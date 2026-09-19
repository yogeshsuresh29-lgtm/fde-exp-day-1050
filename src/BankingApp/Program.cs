using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BankingApp;
using BankingApp.Agent;
using BankingApp.Data;
using BankingApp.Mcp;
using BankingApp.Telemetry;
using BankingApp.Tools;
using DotNetEnv;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

Env.Load();

var builder = WebApplication.CreateBuilder(args);

var fde = new FdeOptions(builder.Configuration);

// ---- Telemetry: ONE OTel pipeline, built once. The participant-attribute
// span processor is the consolidated host's replacement for the two separate
// OTel setups the two source projects each had â€” no double registration.
// Built explicitly via Sdk.CreateTracerProviderBuilder() instead of the DI
// hosted AddOpenTelemetry() registration: a transitive package (Microsoft.Agents /
// Microsoft.Extensions.AI) also calls AddOpenTelemetry(), and its provider
// shadows this one in DI, silently swallowing the OTLP exporter so no traces
// ever left the app. This instance is owned here and disposed after app.Run()
// so the batch processor flushes its queue on shutdown.
TracerProvider? langfuseTracer = null;
if (!string.IsNullOrWhiteSpace(fde.LangfuseOtlpEndpoint))
{
    langfuseTracer = Sdk.CreateTracerProviderBuilder()
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("banking-app"))
        .AddSource(BankingActivitySources.Name)
        .AddProcessor(new ParticipantAttributeProcessor(fde))
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(fde.LangfuseOtlpEndpoint);
            options.Protocol = OtlpExportProtocol.HttpProtobuf;
            if (!string.IsNullOrWhiteSpace(fde.LangfuseOtlpHeaders))
            {
                options.Headers = fde.LangfuseOtlpHeaders;
            }
        })
        .Build();
    Console.WriteLine($"[telemetry] Langfuse OTLP export enabled -> {fde.LangfuseOtlpEndpoint}");
}
else
{
    Console.WriteLine("[telemetry] Langfuse OTLP export DISABLED (no endpoint) - set FDE_LANGFUSE_BASE_URL or FDE_LANGFUSE_OTLP_ENDPOINT");
}

// ---- DI: the consolidated host's services, combining the two source
// projects' registrations into one lifetime scope.
builder.Services.AddSingleton(fde);
builder.Services.AddSingleton<BankingDbConnectionFactory>();
builder.Services.AddSingleton<BankingDbSeeder>();
builder.Services.AddSingleton<AccountTools>();
builder.Services.AddSingleton<McpToolCatalog>();
builder.Services.AddSingleton<McpAgentRuntime>();
builder.Services.AddSingleton<McpHttpEndpoint>();

var app = builder.Build();

// Output-side system-prompt disclosure guard: if the agent echoes a
// distinctive SystemPrompt.md line, /chat returns a denial instead. This is the
// deterministic enforcement for M3 S3 â€” the prompt's own rules are probabilistic
// by nature and cannot be trusted to stop an echo.
var promptGuard = SystemPromptGuard.Load(Path.Combine(app.Environment.ContentRootPath, "SystemPrompt.md"));

// The seed is idempotent. On Container Apps ephemeral storage the baked
// legacy_bank.db copy lands in a writable overlay the first boot; in the
// rare case it can't be written the app keeps serving the read tools anyway.
try
{
    app.Services.GetRequiredService<BankingDbSeeder>().SeedIfMissing();
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Unable to seed legacy_bank.db â€” continuing (tools will report missing data if the file is absent).");
}

// ---- Health endpoints. cd.yml needs BOTH:
//  * /health        â€” the direct-FQDN readiness polling step
//  * /mcp/health    â€” the gateway-reachability step, which can only be
//                     reached as {base}/{participant}/mcp/health because the
//                     shared agentgateway regex only matches under /mcp.
app.MapGet("/health", () => Results.Text("ok"));
app.MapGet("/mcp/health", () => Results.Text("ok"));

// ---- Chat contract used by eval (Milestone 2/3):
// POST {url} and POST {url}/chat both accept either JSON {"message":"..."} or
// a plain-text body, and both return {"reply": "...", "traceId": "..."} with
// the trace id also echoed back on the x-fde-trace-id response header so the
// eval can attach its Langfuse score to the same trace.
app.MapPost("/chat", HandleChatRequest);
app.MapPost("/", HandleChatRequest);

// ---- MCP protocol endpoint (streamable-http compatible surface for the
// shared agent gateway) + a GET convenience that discloses capabilities.
app.MapPost("/mcp", (McpHttpEndpoint endpoint, HttpRequest request) => endpoint.HandleAsync(request));
app.MapGet("/mcp", (McpHttpEndpoint endpoint) => Results.Json(endpoint.Capabilities()));

app.Run();

// Flush + dispose the Langfuse tracer so the batch processor drains queued spans.
langfuseTracer?.Dispose();

// Minimal-API local function â€” declared after app.Run() so top-level
// statements precede the type/method-free body as required.
async Task<IResult> HandleChatRequest(HttpRequest request, HttpResponse response, McpAgentRuntime agent)
{
    var message = await ReadMessageAsync(request);

    // Read X-Session-Customer-Id header; pass it explicitly to the agent which
    // builds a customer-scoped MCP bridge. Without the header, the agent uses
    // the default scope from environment variables (Maria Chen / account 101).
    int? customerId = null;
    if (request.Headers.TryGetValue("X-Session-Customer-Id", out var customerIdValues)
        && int.TryParse(customerIdValues.FirstOrDefault(), out var parsed)
        && parsed > 0)
    {
        customerId = parsed;
    }

    // Wire-transfer amount cross-check: store the original user message on the
    // default AccountTools singleton (best-effort; per-customer bridges use a
    // different instance and won't see this).
    request.HttpContext.RequestServices.GetRequiredService<AccountTools>().SetCurrentMessage(message);

    // Langfuse session context: set BEFORE the root activity starts so the span
    // processor stamps langfuse.session.id on every span of the trace (root and
    // agent children) and Langfuse groups the run into one Session object.
    SessionScope.Set(
        customerId is { } resolved && resolved > 0
            ? $"{fde.ParticipantId}-{fde.PodId}-cust{resolved}"
            : fde.SessionId,
        fde.ParticipantId);

    // ASP.NET Core's hosting layer has already set a source-less, non-recording
    // Activity.Current (Microsoft.AspNetCore.Hosting.HttpRequestIn). Starting a
    // child transport span under it would make .NET return null (not sampled),
    // so no span would ever be created nor exported. Hand the source an explicit
    // recorded parent context instead: .NET then samples the chat span and it
    // becomes the OTLP/Langfuse trace root (with the HTTP request as lineage).
    var ambientCurrent = Activity.Current;
    var parentCtx = ambientCurrent is not null
        ? new ActivityContext(ambientCurrent.TraceId, ambientCurrent.SpanId, ActivityTraceFlags.Recorded, ambientCurrent.TraceStateString)
        : default;
    using var activity = BankingActivitySources.Source.StartActivity("bankingapp.chat", ActivityKind.Server, parentCtx);
    activity?.SetTag("fde.message", message);

    string reply;
    try
    {
        reply = await agent.RunAsync(message, customerId, rawUserMessage: message, cancellationToken: request.HttpContext.RequestAborted);
        reply = promptGuard.Apply(reply);
    }
    catch (Exception ex)
    {
        // Always log the upstream/agent failure â€” without this a gateway 400 or
        // tool-call error surfaces only as an opaque 500 with empty container logs.
        app.Logger.LogError(ex, "chat request failed; message={Message}", message);
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        return Results.Problem(ex.Message, statusCode: 500);
    }

    activity?.SetTag("fde.reply_present", !string.IsNullOrWhiteSpace(reply));

    var traceId = (Activity.Current ?? activity)?.TraceId.ToHexString() ?? "";
    response.Headers["x-fde-trace-id"] = traceId;
    return Results.Json(new { response=reply, traceId });
}

static async Task<string> ReadMessageAsync(HttpRequest request)
{
    if (request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
    {
        var document = await JsonDocument.ParseAsync(request.Body);
        using (document)
        {
            // Guard against a scalar/array JSON body: TryGetProperty throws on a
            // non-object root, which would 500 with no body before the run-catch.
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? "";
            }
        }
    }
    else
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();
        if (!string.IsNullOrWhiteSpace(body))
        {
            return body;
        }
    }

    return "";
}


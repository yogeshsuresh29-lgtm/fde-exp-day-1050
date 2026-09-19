using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;

namespace BankingApp.Mcp;

/// <summary>
/// Self-contained MCP streamable-HTTP-compatible protocol handler mounted at
/// /mcp. ModelContextProtocol 1.2.0 ships the stream transports but does not
/// expose a JSON-RPC parse-and-dispatch helper over HTTP in this version, so
/// the consolidated host wires the protocol surface (initialize, tools/list,
/// tools/call, ping) over the SAME McpToolCatalog the in-process agent uses —
/// guaranteeing the /mcp protocol surface and the in-process agent tools stay
/// identical. Unknown methods return a JSON-RPC error object.
/// </summary>
public sealed class McpHttpEndpoint
{
    private const string ProtocolVersion = "2025-03-26";
    private readonly McpToolCatalog _catalog;

    public McpHttpEndpoint(McpToolCatalog catalog)
    {
        _catalog = catalog;
    }

    public object Capabilities() => new
    {
        jsonrpc = "2.0",
        result = new
        {
            protocolVersion = ProtocolVersion,
            capabilities = new { tools = new { listChanged = false } },
            serverInfo = new { name = "banking-app", version = "1.0.0" },
        },
    };

    public async Task<IResult> HandleAsync(HttpRequest request)
    {
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(request.Body);
        }
        catch (JsonException)
        {
            return Results.Json(ErrorResponse(null, -32700, "Parse error"), contentType: "application/json", statusCode: 400);
        }

        using (document)
        {
            var root = document.RootElement;
            if (!root.TryGetProperty("method", out var methodElement) ||
                methodElement.GetString() is not { Length: > 0 } method)
            {
                return Results.Json(ErrorResponse(null, -32600, "Invalid Request"), contentType: "application/json", statusCode: 400);
            }

            var id = root.TryGetProperty("id", out var idElement) ? ValueFrom(idElement) : null;
            var @params = root.TryGetProperty("params", out var paramsElement)
                ? (JsonElement?)paramsElement
                : null;

            try
            {
                return method switch
                {
                    "initialize" => Results.Json(Success(id, BuildInitializeResult(@params))),
                    "tools/list" => Results.Json(Success(id, BuildToolsListResult())),
                    "tools/call" => await HandleToolsCallAsync(id, @params),
                    "ping" => Results.Json(Success(id, new { })),
                    "notifications/initialized" => Results.Json(Success(id, new { })),
                    _ => Results.Json(ErrorResponse(id, -32601, $"Method not found: {method}"), contentType: "application/json", statusCode: 404),
                };
            }
            catch (McpToolNotFoundException ex)
            {
                return Results.Json(ErrorResponse(id, -32602, ex.Message), contentType: "application/json", statusCode: 200);
            }
            catch (Exception ex)
            {
                return Results.Json(ErrorResponse(id, -32603, ex.Message), contentType: "application/json", statusCode: 200);
            }
        }
    }

    private async Task<IResult> HandleToolsCallAsync(object? id, JsonElement? @params)
    {
        if (@params is not { ValueKind: JsonValueKind.Object } p ||
            !p.TryGetProperty("name", out var nameElement) ||
            nameElement.GetString() is not { } name)
        {
            return Results.Json(ErrorResponse(id, -32602, "tools/call requires a 'name'"), contentType: "application/json", statusCode: 200);
        }

        var arguments = p.TryGetProperty("arguments", out var argElement) ? (JsonElement?)argElement : null;
        var text = _catalog.Invoke(name, arguments);
        var content = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text });
        return Results.Json(Success(id, new JsonObject { ["content"] = content, ["isError"] = false }));
    }

    private object BuildInitializeResult(JsonElement? @params)
    {
        // If the client sends traces in initialize, only the pre-agreed
        // protocol version is echoed back (Langfuse attach points to the
        // request traceId via response headers/body from /chat).
        return new
        {
            protocolVersion = ProtocolVersion,
            capabilities = new { tools = new { listChanged = false } },
            serverInfo = new { name = "banking-app", version = "1.0.0" },
        };
    }

    private object BuildToolsListResult()
    {
        var tools = new JsonArray();
        foreach (var definition in _catalog.Definitions)
        {
            var properties = new JsonObject();
            var required = new JsonArray();
            foreach (var parameter in definition.Parameters)
            {
                properties[parameter.Name] = JsonNode.Parse(JsonSerializer.Serialize(new
                {
                    type = JsonTypeName(parameter.Type),
                }))!;
                if (parameter.Required)
                {
                    required.Add(parameter.Name);
                }
            }

            tools.Add(new JsonObject
            {
                ["name"] = definition.Name,
                ["description"] = definition.Description,
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = properties,
                    ["required"] = required,
                },
            });
        }

        return new JsonObject { ["tools"] = tools };
    }

    private static string JsonTypeName(Type type) => type switch
    {
        not null when type == typeof(int) => "integer",
        not null when type == typeof(decimal) || type == typeof(double) => "number",
        _ => "string",
    };

    private static object Success(object? id, object result) => new
    {
        jsonrpc = "2.0",
        id,
        result,
    };

    private static object ErrorResponse(object? id, int code, string message) => new
    {
        jsonrpc = "2.0",
        id,
        error = new { code, message },
    };

    private static object? ValueFrom(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Number when element.TryGetInt64(out var value) => value,
        JsonValueKind.String => element.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => element.ToString(),
    };
}
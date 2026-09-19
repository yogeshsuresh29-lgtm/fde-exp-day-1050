using System.Reflection;
using System.Text.Json;
using BankingApp.Tools;
using ModelContextProtocol.Server;

namespace BankingApp.Mcp;

/// <summary>
/// Single source of truth for the MCP tool surface. The same method metadata
/// feeds (a) the in-process MCP server's ToolCollection (what the agent's
/// self-bound McpClient sees), (b) the JSON-RPC /mcp protocol endpoint, and
/// (c) the deterministic mock-agent executor used in CI.
/// </summary>
public sealed class McpToolCatalog
{
    private readonly IReadOnlyList<McpToolDefinition> _definitions;

    public McpToolCatalog(AccountTools accounts)
    {
        var phone = new PhoneNormalizer();

        _definitions =
        [
            Define("get_balance",
                "Gets the current balance of an account by its numeric id.",
                "int", true, accounts, nameof(AccountTools.GetBalance)),

            Define("list_accounts",
                "Lists every account on file with current balances.",
                null, false, accounts, nameof(AccountTools.ListAccounts)),

            Define("get_transaction_history",
                "Gets the most recent transactions for an account by id.",
                "int", true, accounts, nameof(AccountTools.GetTransactionHistory), "limit", "int", false),

            Define("normalize_phone",
                "Normalizes a US phone number to +1XXXXXXXXXX.",
                "string", true, phone, nameof(PhoneNormalizer.NormalizePhone)),

            Define("submit_wire_transfer",
                "Submits a wire transfer. Amounts above the configured threshold are paused pending approval; smaller amounts post.",
                "int", true, accounts, nameof(AccountTools.SubmitWireTransfer), "toAccountId", "int", true, "amount", "decimal", true, "memo", "string", false),
        ];
    }

    public IReadOnlyList<McpToolDefinition> Definitions => _definitions;

    public McpToolDefinition? Find(string name) =>
        _definitions.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>McpServerTool list for the in-process MCP server's ToolCollection.</summary>
    public IReadOnlyList<McpServerTool> ForMcpServer()
    {
        return _definitions.Select(d =>
            McpServerTool.Create(d.Method, d.Target, new McpServerToolCreateOptions
            {
                Name = d.Name,
                Description = d.Description,
            })).ToArray();
    }

    /// <summary>
    /// Invokes a tool by name with a JSON-arguments object. Deterministic text
    /// result — shared by the /mcp protocol handler and the mock agent.
    /// </summary>
    public string Invoke(string name, JsonElement? arguments)
    {
        var definition = Find(name)
            ?? throw new McpToolNotFoundException($"Tool '{name}' is not supported by this agent.");

        var values = new object?[definition.Parameters.Count];
        for (var i = 0; i < definition.Parameters.Count; i++)
        {
            var parameter = definition.Parameters[i];
            if (arguments is { } args && args.ValueKind == JsonValueKind.Object &&
                args.TryGetProperty(parameter.Name, out var element))
            {
                values[i] = ConvertArgument(element, parameter.Type);
            }
            else if (!parameter.Required)
            {
                values[i] = parameter.Type == typeof(int)
                    ? 5
                    : parameter.Type == typeof(string) ? "" : null;
            }
            else
            {
                throw new ArgumentException($"Parameter '{parameter.Name}' is required.");
            }
        }

        try
        {
            var result = definition.Method.Invoke(definition.Target, values);
            return result?.ToString() ?? "(no result)";
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static McpToolDefinition Define(
        string name,
        string description,
        string? argType,
        bool required,
        object target,
        string methodName,
        params object[] extra)
    {
        var method = target.GetType().GetMethod(methodName)
            ?? throw new InvalidOperationException($"Method {methodName} not found on {target.GetType().Name}.");

        var parameters = new List<McpToolParameter>();
        if (argType is not null)
        {
            parameters.Add(new McpToolParameter(ParameterNameFromMethod(method, 0), TypeFrom(argType), required));
        }

        for (var i = 0; i + 2 < extra.Length; i += 3)
        {
            parameters.Add(new McpToolParameter((string)extra[i]!, TypeFrom((string)extra[i + 1]!), (bool)extra[i + 2]!));
        }

        return new McpToolDefinition(name, description, parameters, method, target);
    }

    private static string ParameterNameFromMethod(MethodInfo method, int index)
    {
        var parameter = method.GetParameters()[index];
        return parameter.Name ?? $"param{index}";
    }

    private static Type TypeFrom(string type) => type switch
    {
        "int" => typeof(int),
        "decimal" => typeof(decimal),
        "string" => typeof(string),
        _ => typeof(object),
    };

    private static object ConvertArgument(JsonElement element, Type type)
    {
        if (type == typeof(int)) return element.GetInt32();
        if (type == typeof(decimal)) return element.GetDecimal();
        if (type == typeof(string)) return element.GetString() ?? "";
        return element.ToString();
    }
}

/// <summary>Signature + metadata for one exposed MCP tool.</summary>
public sealed record McpToolDefinition(
    string Name,
    string Description,
    IReadOnlyList<McpToolParameter> Parameters,
    MethodInfo Method,
    object? Target);

public sealed record McpToolParameter(string Name, Type Type, bool Required);

public sealed class McpToolNotFoundException(string message) : Exception(message);

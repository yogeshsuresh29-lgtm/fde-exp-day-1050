using BankingApp;
using BankingApp.Data;
using Microsoft.Data.Sqlite;

namespace BankingApp.Tools;

/// <summary>
/// Read-only account-query tools. Moved/rewritten into the consolidated
/// BankingApp from the Session 2 scaffold's BankingMcpServer. All writes
/// (wire transfers) are simulated and gate on the participant's wired
/// threshold so Milestone 3 can observe a deterministic PAUSED state.
///
/// Scope enforcement lives HERE, in code, not in the system prompt: every read
/// tool refuses any account outside <see cref="FdeOptions.SessionAccountIds"/>
/// (the authenticated session's allowed set). The model cannot talk its way past
/// a WHERE clause, so the plain-language rules in SystemPrompt.md are a
/// belt-and-suspenders layer on top of a deterministic check, not the check.
/// </summary>
public sealed class AccountTools
{
    private readonly BankingDbConnectionFactory _db;
    private readonly FdeOptions _fde;
    private readonly int _sessionCustomerId;
    private readonly IReadOnlyList<int> _sessionAccountIds;
private readonly AsyncLocal<IReadOnlyList<int>?> _currentSessionAccountIds = new();
private volatile string? _currentUserMessage;
private readonly string? _transferRawMessage;

    public AccountTools(BankingDbConnectionFactory db, FdeOptions fde)
    {
        _db = db;
        _fde = fde;
        _sessionCustomerId = fde.SessionCustomerId;
        _sessionAccountIds = fde.SessionAccountIds;
    }

    /// <summary>
    /// Creates an AccountTools instance bound to a specific customer. The customer's
    /// account IDs are resolved from the database at construction time — this instance
    /// is permanently scoped to that customer. No AsyncLocal, no ambient state.
    /// Used by McpAgentRuntime when building a per-customer MCP bridge.
    /// </summary>
    public AccountTools(BankingDbConnectionFactory db, FdeOptions fde, int customerId, string? rawUserMessage = null)
    {
        _db = db;
        _fde = fde;
        _sessionCustomerId = customerId;
        _sessionAccountIds = GetAccountIdsForCustomer(db, customerId);
        _transferRawMessage = rawUserMessage;
    }

    private IReadOnlyList<int> EffectiveAccountIds =>
        _currentSessionAccountIds.Value ?? _sessionAccountIds;

    /// <summary>
    /// Sets the session account scope for the current async context. This overrides
    /// the environment-variable defaults, enabling per-request identity when a caller
    /// supplies an X-Session-Customer-Id header. The override is AsyncLocal-scoped
    /// so concurrent /chat requests do not interfere with each other.
    /// </summary>
    public void SetSessionAccountIds(IReadOnlyList<int> accountIds)
    {
        _currentSessionAccountIds.Value = accountIds;
    }

    /// <summary>
    /// Stores the original user message so SubmitWireTransfer can cross-check that
    /// the model's extracted amount matches the user's actual request. Called by
    /// HandleChatRequest before each agent invocation.
    /// </summary>
    internal void SetCurrentMessage(string message)
    {
        _currentUserMessage = message;
    }

    /// <summary>Static overload for use during construction (before _db is assigned).</summary>
    private static IReadOnlyList<int> GetAccountIdsForCustomer(BankingDbConnectionFactory db, int customerId)
    {
        using var connection = db.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM accounts WHERE customer_id = $customerId ORDER BY id";
        command.Parameters.AddWithValue("$customerId", customerId);

        var ids = new List<int>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            ids.Add(reader.GetInt32(0));
        }
        return ids;
    }

    /// <summary>
    /// Resolves all account ids owned by a given customer from the database.
    /// Used by the /chat handler when an X-Session-Customer-Id header is present.
    /// </summary>
    public IReadOnlyList<int> GetAccountIdsForCustomer(int customerId)
    {
        return GetAccountIdsForCustomer(_db, customerId);
    }

    private static string Denied(int accountId, IReadOnlyList<int> allowed) =>
        $"DENIED: account {accountId} is outside the authenticated session's scope (session accounts: {string.Join(", ", allowed)}).";

    public string GetBalance(int accountId)
    {
        var allowed = EffectiveAccountIds;
        if (!allowed.Contains(accountId))
        {
            return Denied(accountId, allowed);
        }

        using var connection = _db.Create();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.name, a.account_number, a.name, a.balance_cents, a.currency
            FROM accounts a JOIN customers c ON c.id = a.customer_id
            WHERE a.id = $id
            """;
        command.Parameters.AddWithValue("$id", accountId);
        using var reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return $"Account {accountId}: not found";
        }

        var customer = reader.GetString(0);
        var number = reader.GetString(1);
        var accountName = reader.GetString(2);
        var cents = reader.GetInt64(3);
        var currency = reader.GetString(4);
        return $"{customer} {accountName} (#{number}): {FormatMoney(cents, currency)}";
    }

    public string ListAccounts()
    {
        var allowed = EffectiveAccountIds;
        if (allowed.Count == 0)
        {
            return "no accounts in the authenticated session's scope";
        }

        using var connection = _db.Create();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT c.name, a.id, a.account_number, a.name, a.balance_cents, a.currency
            FROM accounts a JOIN customers c ON c.id = a.customer_id
            WHERE a.id IN ({string.Join(", ", allowed)})
            ORDER BY a.id
            """;

        var lines = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var customer = reader.GetString(0);
            var id = reader.GetInt64(1);
            var number = reader.GetString(2);
            var accountName = reader.GetString(3);
            var cents = reader.GetInt64(4);
            var currency = reader.GetString(5);
            lines.Add($"{customer} {accountName} (#{number}, id {id}): {FormatMoney(cents, currency)}");
        }

        return lines.Count == 0 ? "no accounts found" : string.Join("\n", lines);
    }

    public string GetTransactionHistory(int accountId, int limit = 5)
    {
        var allowed = EffectiveAccountIds;
        if (!allowed.Contains(accountId))
        {
            return Denied(accountId, allowed);
        }

        using var connection = _db.Create();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT t.description, t.amount_cents, a.currency, t.occurred_at
            FROM transactions t JOIN accounts a ON a.id = t.account_id
            WHERE t.account_id = $id
            ORDER BY t.occurred_at DESC, t.id DESC
            LIMIT $limit
            """;
        command.Parameters.AddWithValue("$id", accountId);
        command.Parameters.AddWithValue("$limit", Math.Max(1, limit));

        var lines = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var description = reader.GetString(0);
            var cents = reader.GetInt64(1);
            var currency = reader.GetString(2);
            var when = reader.GetString(3);
            lines.Add($"{when} {description}: {FormatMoney(cents, currency)}");
        }

        return lines.Count == 0 ? $"account {accountId}: no transactions" : string.Join("\n", lines);
    }

    /// <summary>
    /// Simulated wire transfer. Over-threshold transfers are PAUSED and await
    /// approval (Milestone 3 checks this explicit paused state); under-threshold
    /// transfers are POSTED. The account ledger is intentionally read-only — the
    /// "bank" is a baked, ephemeral seed, so no actual balance mutation.
    /// </summary>
    public string SubmitWireTransfer(int fromAccountId, int toAccountId, decimal amount, string memo = "")
    {
        // Cross-check: if the user's original message contains an explicit dollar amount,
        // verify the model's extracted amount matches one of them. A mismatch means the
        // model misparsed the user's intent - reject rather than executing the wrong amount.
        var mismatch = CrossCheckAmount(amount);
        if (mismatch is not null)
        {
            return mismatch;
        }

        if (amount <= 0)
        {
            return $"ERROR: transfer amount must be positive (received {amount:C})";
        }

        var threshold = _fde.WireTransferThreshold;
        if (amount > threshold)
        {
            return $"PAUSED_PENDING_APPROVAL: ${amount:0.00} from account {fromAccountId} to account {toAccountId} " +
                   $"exceeds the ${threshold:0.00} wire threshold and requires explicit approval before it can post.";
        }

        return $"POSTED: ${amount:0.00} from account {fromAccountId} to account {toAccountId}" +
               (string.IsNullOrWhiteSpace(memo) ? "" : $" ({memo})");
    }

    private static string FormatMoney(long cents, string currency)
    {
        var amount = cents / 100m;
        return currency == "USD" ? $"${amount:0.00}" : $"{amount:0.00} {currency}";
    }

    /// <summary>
    /// Extracts dollar amounts from the original user message and compares against the
    /// model's amount. Returns null if they match (or if no dollar amount found in message).
    /// Returns an error string if the model's amount doesn't match any parsed amount.
    /// </summary>
    private string? CrossCheckAmount(decimal modelAmount)
    {
        var message = _transferRawMessage ?? _currentUserMessage;
        if (string.IsNullOrWhiteSpace(message))
            return null;

        // Extract all $X, $X.XX, $X,XXX, $X,XXX.XX patterns
        var matches = System.Text.RegularExpressions.Regex.Matches(message, @"\$([\d,]+(?:\.\d{2})?)");
        if (matches.Count == 0)
            return null;

        var parsedAmounts = new List<decimal>();
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var raw = match.Groups[1].Value.Replace(",", "");
            if (decimal.TryParse(raw, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                parsedAmounts.Add(parsed);
            }
        }

        if (parsedAmounts.Count == 0)
            return null;

        if (parsedAmounts.Contains(modelAmount))
            return null;

        return $"AMOUNT_MISMATCH: could not verify the requested transfer amount, transfer blocked.";
    }
}

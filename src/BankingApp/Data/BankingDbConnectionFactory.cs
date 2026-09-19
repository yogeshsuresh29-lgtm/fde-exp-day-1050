using Microsoft.Data.Sqlite;

namespace BankingApp.Data;

/// <summary>
/// Resolves the SQLite database path from configuration and hands out opened
/// connections. The intended key is <c>BankingDb:Path</c>. appsettings.Production.json
/// deliberately ships the WRONG key name (<c>BankingDb:WrongPath</c>) — the 09:20
/// friction point — so the fallback to the content-root baked seed keeps the app
/// working (PromptDefense flags the wrong key, not the app's runtime).
/// </summary>
public sealed class BankingDbConnectionFactory
{
    public string DbPath { get; }

    public BankingDbConnectionFactory(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration["BankingDb:Path"];
        DbPath = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(environment.ContentRootPath, "legacy_bank.db")
            : Path.GetFullPath(configured, environment.ContentRootPath);
    }

    public SqliteConnection Create()
    {
        // ReadWriteCreate is the default mode — the seed is idempotent, so the
        // first boot in ephemeral Container Apps storage re-creates the legacy
        // file if the baked copy is missing or read-only.
        var connection = new SqliteConnection($"Data Source={DbPath}");
        connection.Open();
        return connection;
    }
}
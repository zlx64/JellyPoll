using Dapper;
using MediaBrowser.Common.Configuration;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.JellyPoll.Data;

/// <summary>
/// SQLite bootstrap and connection factory (doc 02 §1, §5).
/// WAL mode, foreign keys on, busy_timeout, user_version-based migrations.
/// </summary>
public sealed class Db
{
    private readonly string _dbPath;

    public Db(IApplicationPaths applicationPaths)
    {
        var dir = Path.Combine(applicationPaths.PluginConfigurationsPath, "JellyPoll");
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, "jellypoll.db");
    }

    /// <summary>Database file path (exposed for diagnostics/tests).</summary>
    public string DbPath => _dbPath;

    /// <summary>For tests: create a Db at an arbitrary path.</summary>
    public Db(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _dbPath = dbPath;
    }

    public SqliteConnection Open()
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _dbPath }.ToString());
        conn.Open();
        conn.Execute("PRAGMA journal_mode=WAL;");
        conn.Execute("PRAGMA foreign_keys=ON;");
        conn.Execute("PRAGMA busy_timeout=5000;");
        return conn;
    }

    /// <summary>Creates the schema if absent and applies pending migrations.</summary>
    public void Initialize()
    {
        using var conn = Open();
        var version = conn.ExecuteScalar<long>("PRAGMA user_version;");
        if (version < 1)
        {
            conn.Execute(SchemaV1);
            conn.Execute("PRAGMA user_version = 1;");
        }
    }

    private const string SchemaV1 = @"
CREATE TABLE IF NOT EXISTS polls (
    id             TEXT PRIMARY KEY,
    title          TEXT NOT NULL,
    status         INTEGER NOT NULL DEFAULT 0 CHECK (status IN (0, 1)),
    created_by     TEXT NOT NULL,
    created_at     TEXT NOT NULL,
    closed_by      TEXT,
    closed_at      TEXT,
    allow_episodes INTEGER NOT NULL DEFAULT 1,
    allow_series   INTEGER NOT NULL DEFAULT 1,
    state_version  INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS suggestions (
    id           TEXT PRIMARY KEY,
    poll_id      TEXT NOT NULL REFERENCES polls(id) ON DELETE CASCADE,
    item_id      TEXT NOT NULL,
    item_type    TEXT NOT NULL CHECK (item_type IN ('Movie', 'Episode', 'Series')),
    item_name    TEXT NOT NULL,
    item_year    INTEGER,
    suggested_by TEXT NOT NULL,
    suggested_at TEXT NOT NULL,
    UNIQUE (poll_id, item_id)
);

CREATE TABLE IF NOT EXISTS ballots (
    id         TEXT PRIMARY KEY,
    poll_id    TEXT NOT NULL REFERENCES polls(id) ON DELETE CASCADE,
    user_id    TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    UNIQUE (poll_id, user_id)
);

CREATE TABLE IF NOT EXISTS ballot_entries (
    ballot_id     TEXT NOT NULL REFERENCES ballots(id) ON DELETE CASCADE,
    suggestion_id TEXT NOT NULL REFERENCES suggestions(id) ON DELETE CASCADE,
    position      INTEGER NOT NULL CHECK (position >= 1),
    PRIMARY KEY (ballot_id, suggestion_id)
);

CREATE INDEX IF NOT EXISTS idx_suggestions_poll      ON suggestions (poll_id);
CREATE INDEX IF NOT EXISTS idx_ballots_poll          ON ballots (poll_id);
CREATE INDEX IF NOT EXISTS idx_ballot_entries_ballot ON ballot_entries (ballot_id);
";
}

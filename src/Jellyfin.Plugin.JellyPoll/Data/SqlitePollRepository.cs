using Dapper;
using Jellyfin.Plugin.JellyPoll.Data;
using Microsoft.Data.Sqlite;
using System.Data;

namespace Jellyfin.Plugin.JellyPoll.Data;

/// <summary>
/// All poll persistence (doc 02 §6, §7).
/// Writes run under an app-level write lock inside transactions;
/// state_version is bumped in the same transaction as every mutation.
/// </summary>
public sealed class SqlitePollRepository : IPollRepository
{
    private readonly Db _db;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public SqlitePollRepository(Db db)
    {
        _db = db;
    }

    public void Initialize() => _db.Initialize();

    // ---------- helpers ----------

    private static string Now() => DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);

    private const string PollCols = "id AS Id, title AS Title, status AS Status, created_by AS CreatedBy, created_at AS CreatedAt, closed_by AS ClosedBy, closed_at AS ClosedAt, allow_episodes AS AllowEpisodes, allow_series AS AllowSeries, state_version AS StateVersion";
    private const string SuggestionCols = "id AS Id, poll_id AS PollId, item_id AS ItemId, item_type AS ItemType, item_name AS ItemName, item_year AS ItemYear, suggested_by AS SuggestedBy, suggested_at AS SuggestedAt";

    private static PollRow MapPoll(dynamic row) => new()
    {
        Id = Guid.Parse(row.Id),
        Title = row.Title,
        Status = (PollStatus)(long)row.Status,
        CreatedBy = Guid.Parse(row.CreatedBy),
        CreatedAt = row.CreatedAt,
        ClosedBy = row.ClosedBy is null ? null : Guid.Parse(row.ClosedBy),
        ClosedAt = row.ClosedAt,
        AllowEpisodes = Convert.ToBoolean(row.AllowEpisodes),
        AllowSeries = Convert.ToBoolean(row.AllowSeries),
        StateVersion = (int)(long)row.StateVersion
    };

    private static SuggestionRow MapSuggestion(dynamic row) => new()
    {
        Id = Guid.Parse(row.Id),
        PollId = Guid.Parse(row.PollId),
        ItemId = Guid.Parse(row.ItemId),
        ItemType = row.ItemType,
        ItemName = row.ItemName,
        ItemYear = row.ItemYear is null ? null : (int)(long)row.ItemYear,
        SuggestedBy = Guid.Parse(row.SuggestedBy),
        SuggestedAt = row.SuggestedAt
    };

    private static void BumpStateVersion(SqliteConnection conn, IDbTransaction tx, Guid pollId)
        => conn.Execute("UPDATE polls SET state_version = state_version + 1 WHERE id = @id;",
            new { id = pollId.ToString() }, tx);

    // ---------- polls ----------

    public void CreatePoll(PollRow poll)
    {
        _writeLock.Wait();
        try
        {
            using var conn = _db.Open();
            using var tx = conn.BeginTransaction();
            conn.Execute(
                "INSERT INTO polls (id, title, status, created_by, created_at, closed_by, closed_at, allow_episodes, allow_series, state_version) " +
                "VALUES (@id, @title, 0, @createdBy, @createdAt, NULL, NULL, @allowEpisodes, @allowSeries, 1);",
                new
                {
                    id = poll.Id.ToString(),
                    title = poll.Title,
                    createdBy = poll.CreatedBy.ToString(),
                    createdAt = Now(),
                    allowEpisodes = poll.AllowEpisodes ? 1 : 0,
                    allowSeries = poll.AllowSeries ? 1 : 0
                },
                tx);
            tx.Commit();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public PollRow? GetPoll(Guid pollId)
    {
        using var conn = _db.Open();
        var row = conn.QueryFirstOrDefault(
            "SELECT " + PollCols + " FROM polls WHERE id = @id",
            new { id = pollId.ToString() });
        return row is null ? null : MapPoll(row);
    }

    public IReadOnlyList<PollRow> ListPolls()
    {
        using var conn = _db.Open();
        const string sql = "SELECT " + PollCols + " FROM polls " +
                           "ORDER BY status ASC, " +
                           "CASE WHEN status = 0 THEN created_at END DESC, " +
                           "CASE WHEN status = 1 THEN closed_at END DESC, " +
                           "created_at DESC;";
        var rows = conn.Query(sql);
        return rows.Select(MapPoll).ToList();
    }

    public void ClosePoll(Guid pollId, Guid closedBy)
    {
        _writeLock.Wait();
        try
        {
            using var conn = _db.Open();
            using var tx = conn.BeginTransaction();
            conn.Execute(
                "UPDATE polls SET status = 1, closed_by = @closedBy, closed_at = @closedAt WHERE id = @id AND status = 0;",
                new { closedBy = closedBy.ToString(), closedAt = Now(), id = pollId.ToString() }, tx);
            BumpStateVersion(conn, tx, pollId);
            tx.Commit();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void ReopenPoll(Guid pollId)
    {
        _writeLock.Wait();
        try
        {
            using var conn = _db.Open();
            using var tx = conn.BeginTransaction();
            conn.Execute(
                "UPDATE polls SET status = 0, closed_by = NULL, closed_at = NULL WHERE id = @id AND status = 1;",
                new { id = pollId.ToString() }, tx);
            BumpStateVersion(conn, tx, pollId);
            tx.Commit();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void DeletePoll(Guid pollId)
    {
        _writeLock.Wait();
        try
        {
            using var conn = _db.Open();
            using var tx = conn.BeginTransaction();
            // Children cascade; delete explicitly for pragma safety too.
            conn.Execute(
                "DELETE FROM ballot_entries WHERE ballot_id IN (SELECT id FROM ballots WHERE poll_id = @id);",
                new { id = pollId.ToString() }, tx);
            conn.Execute("DELETE FROM ballots WHERE poll_id = @id;", new { id = pollId.ToString() }, tx);
            conn.Execute("DELETE FROM suggestions WHERE poll_id = @id;", new { id = pollId.ToString() }, tx);
            conn.Execute("DELETE FROM polls WHERE id = @id;", new { id = pollId.ToString() }, tx);
            tx.Commit();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public int DeleteClosedPolls()
    {
        _writeLock.Wait();
        try
        {
            using var conn = _db.Open();
            using var tx = conn.BeginTransaction();
            var count = (int)conn.ExecuteScalar<long>(
                "SELECT COUNT(*) FROM polls WHERE status = 1;", tx);
            conn.Execute(
                "DELETE FROM ballot_entries WHERE ballot_id IN (SELECT id FROM ballots WHERE poll_id IN (SELECT id FROM polls WHERE status = 1));",
                transaction: tx);
            conn.Execute("DELETE FROM ballots WHERE poll_id IN (SELECT id FROM polls WHERE status = 1);", transaction: tx);
            conn.Execute("DELETE FROM suggestions WHERE poll_id IN (SELECT id FROM polls WHERE status = 1);", transaction: tx);
            conn.Execute("DELETE FROM polls WHERE status = 1;", transaction: tx);
            tx.Commit();
            return count;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // ---------- suggestions ----------

    /// <exception cref="DuplicateSuggestionException">UNIQUE(poll_id, item_id) violated.</exception>
    public void AddSuggestion(SuggestionRow suggestion)
    {
        _writeLock.Wait();
        try
        {
            using var conn = _db.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                conn.Execute(
                    "INSERT INTO suggestions (id, poll_id, item_id, item_type, item_name, item_year, suggested_by, suggested_at) " +
                    "VALUES (@id, @pollId, @itemId, @itemType, @itemName, @itemYear, @suggestedBy, @suggestedAt);",
                    new
                    {
                        id = suggestion.Id.ToString(),
                        pollId = suggestion.PollId.ToString(),
                        itemId = suggestion.ItemId.ToString(),
                        itemType = suggestion.ItemType,
                        itemName = suggestion.ItemName,
                        itemYear = suggestion.ItemYear,
                        suggestedBy = suggestion.SuggestedBy.ToString(),
                        suggestedAt = Now()
                    }, tx);
                BumpStateVersion(conn, tx, suggestion.PollId);
                tx.Commit();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19 && ex.SqliteExtendedErrorCode == 2067)
            {
                throw new DuplicateSuggestionException();
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public SuggestionRow? GetSuggestion(Guid pollId, Guid suggestionId)
    {
        using var conn = _db.Open();
        var row = conn.QueryFirstOrDefault(
            "SELECT " + SuggestionCols + " FROM suggestions WHERE poll_id = @p AND id = @s",
            new { p = pollId.ToString(), s = suggestionId.ToString() });
        return row is null ? null : MapSuggestion(row);
    }

    public IReadOnlyList<SuggestionRow> ListSuggestions(Guid pollId)
    {
        using var conn = _db.Open();
        var rows = conn.Query(
            "SELECT " + SuggestionCols + " FROM suggestions WHERE poll_id = @p ORDER BY suggested_at ASC, item_id ASC;",
            new { p = pollId.ToString() });
        return rows.Select(MapSuggestion).ToList();
    }

    public int CountSuggestionsByUser(Guid pollId, Guid userId)
    {
        using var conn = _db.Open();
        return (int)conn.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM suggestions WHERE poll_id = @p AND suggested_by = @u;",
            new { p = pollId.ToString(), u = userId.ToString() });
    }

    public void RemoveSuggestion(Guid pollId, Guid suggestionId)
    {
        _writeLock.Wait();
        try
        {
            using var conn = _db.Open();
            using var tx = conn.BeginTransaction();
            conn.Execute(
                "DELETE FROM ballot_entries WHERE suggestion_id = @s;",
                new { s = suggestionId.ToString() }, tx);
            var affected = conn.Execute(
                "DELETE FROM suggestions WHERE id = @s AND poll_id = @p;",
                new { s = suggestionId.ToString(), p = pollId.ToString() }, tx);
            if (affected == 0)
            {
                throw new SuggestionNotFoundException();
            }

            BumpStateVersion(conn, tx, pollId);
            tx.Commit();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // ---------- ballots ----------

    /// <summary>Replaces the user's ballot atomically; positions renumbered 1..k (doc 02 §6).</summary>
    public void UpsertBallot(Guid pollId, Guid userId, IReadOnlyList<Guid> orderedSuggestionIds)
    {
        _writeLock.Wait();
        try
        {
            using var conn = _db.Open();
            using var tx = conn.BeginTransaction();

            var poll = conn.QueryFirstOrDefault(
                "SELECT status FROM polls WHERE id = @id;", new { id = pollId.ToString() }, tx);
            if (poll is null)
            {
                throw new PollNotFoundException();
            }

            if ((long)poll.status != (long)PollStatus.Open)
            {
                throw new PollClosedException();
            }

            var validIds = conn.Query<string>(
                "SELECT id FROM suggestions WHERE poll_id = @p;", new { p = pollId.ToString() }, tx)
                .ToHashSet();
            foreach (var sid in orderedSuggestionIds)
            {
                if (!validIds.Contains(sid.ToString()))
                {
                    throw new ValidationException($"Suggestion {sid} does not belong to this poll.");
                }
            }

            var ballotId = conn.QueryFirstOrDefault<string>(
                "SELECT id FROM ballots WHERE poll_id = @p AND user_id = @u;",
                new { p = pollId.ToString(), u = userId.ToString() }, tx);
            if (ballotId is null)
            {
                ballotId = Guid.NewGuid().ToString();
                conn.Execute(
                    "INSERT INTO ballots (id, poll_id, user_id, updated_at) VALUES (@id, @p, @u, @now);",
                    new { id = ballotId, p = pollId.ToString(), u = userId.ToString(), now = Now() }, tx);
            }

            conn.Execute("DELETE FROM ballot_entries WHERE ballot_id = @b;", new { b = ballotId }, tx);
            var position = 1;
            foreach (var sid in orderedSuggestionIds)
            {
                conn.Execute(
                    "INSERT INTO ballot_entries (ballot_id, suggestion_id, position) VALUES (@b, @s, @pos);",
                    new { b = ballotId, s = sid.ToString(), pos = position++ }, tx);
            }

            conn.Execute("UPDATE ballots SET updated_at = @now WHERE id = @b;", new { now = Now(), b = ballotId }, tx);
            BumpStateVersion(conn, tx, pollId);
            tx.Commit();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public IReadOnlyList<Guid> GetBallot(Guid pollId, Guid userId)
    {
        using var conn = _db.Open();
        const string sql = "SELECT be.suggestion_id " +
                           "FROM ballots b JOIN ballot_entries be ON be.ballot_id = b.id " +
                           "WHERE b.poll_id = @p AND b.user_id = @u " +
                           "ORDER BY be.position ASC;";
        var rows = conn.Query<string>(sql, new { p = pollId.ToString(), u = userId.ToString() });
        return rows.Select(Guid.Parse).ToList();
    }

    public int GetBallotCountForUser(Guid pollId, Guid userId)
        => GetBallot(pollId, userId).Count;

    public IReadOnlyDictionary<Guid, int> GetBallotCountsForUser(Guid userId)
    {
        using var conn = _db.Open();
        const string sql = "SELECT b.poll_id AS PollId, COUNT(*) AS Cnt " +
                           "FROM ballots b JOIN ballot_entries be ON be.ballot_id = b.id " +
                           "WHERE b.user_id = @u " +
                           "GROUP BY b.poll_id;";
        var rows = conn.Query<(string PollId, int Cnt)>(sql, new { u = userId.ToString() });
        return rows.ToDictionary(r => Guid.Parse(r.PollId), r => r.Cnt);
    }

    public int GetVoterCount(Guid pollId)
    {
        using var conn = _db.Open();
        const string sql = "SELECT COUNT(DISTINCT b.user_id) " +
                           "FROM ballots b JOIN ballot_entries be ON be.ballot_id = b.id " +
                           "WHERE b.poll_id = @p;";
        return (int)conn.ExecuteScalar<long>(sql, new { p = pollId.ToString() });
    }

    public int GetSuggestionCount(Guid pollId)
    {
        using var conn = _db.Open();
        return (int)conn.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM suggestions WHERE poll_id = @p;", new { p = pollId.ToString() });
    }

    /// <summary>Consistent snapshot of suggestions + ballots for scoring (doc 02 §7.4).</summary>
    public PollSnapshot GetSnapshot(Guid pollId)
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var suggestions = conn.Query(
                "SELECT " + SuggestionCols + " FROM suggestions WHERE poll_id = @p;",
                new { p = pollId.ToString() }, tx)
            .Select(MapSuggestion).ToList();

        const string ballotsSql = "SELECT b.user_id AS UserId, be.suggestion_id AS Sid " +
                                  "FROM ballots b JOIN ballot_entries be ON be.ballot_id = b.id " +
                                  "WHERE b.poll_id = @p " +
                                  "ORDER BY b.user_id ASC, be.position ASC;";
        var ballots = new Dictionary<string, List<Guid>>();
        foreach (var row in conn.Query(ballotsSql, new { p = pollId.ToString() }, tx))
        {
            string rowUserId = row.UserId;
            string rowSid = row.Sid;
            if (!ballots.TryGetValue(rowUserId, out var list))
            {
                ballots[rowUserId] = list = new List<Guid>();
            }

            list.Add(Guid.Parse(rowSid));
        }

        return new PollSnapshot(
            suggestions,
            ballots.Select(kv => new BallotSnapshot(Guid.Parse(kv.Key), kv.Value)).ToList());
    }

    public int GetStateVersion(Guid pollId)
    {
        using var conn = _db.Open();
        var v = conn.ExecuteScalar<long?>("SELECT state_version FROM polls WHERE id = @id;", new { id = pollId.ToString() });
        return v is null ? throw new PollNotFoundException() : (int)v;
    }

    // ---------- maintenance ----------

    /// <summary>Removes suggestions whose library item no longer exists. Returns removed count.
    /// state_version is bumped for every poll whose suggestions were touched.</summary>
    public int RemoveSuggestions(IReadOnlyList<Guid> suggestionIds)
    {
        if (suggestionIds.Count == 0)
        {
            return 0;
        }

        _writeLock.Wait();
        try
        {
            using var conn = _db.Open();
            using var tx = conn.BeginTransaction();

            // Collect the affected polls BEFORE deletion so polls whose suggestions are
            // entirely removed still get a state bump.
            var pollIds = new List<Guid>();
            foreach (var sid in suggestionIds)
            {
                var pollId = conn.ExecuteScalar<string?>(
                    "SELECT poll_id FROM suggestions WHERE id = @s;", new { s = sid.ToString() });
                if (pollId is not null && Guid.TryParse(pollId, out var g))
                {
                    pollIds.Add(g);
                }
            }

            var removed = 0;
            foreach (var sid in suggestionIds)
            {
                conn.Execute("DELETE FROM ballot_entries WHERE suggestion_id = @s;", new { s = sid.ToString() }, tx);
                removed += conn.Execute("DELETE FROM suggestions WHERE id = @s;", new { s = sid.ToString() }, tx);
            }

            foreach (var pollId in pollIds.Distinct())
            {
                BumpStateVersion(conn, tx, pollId);
            }

            tx.Commit();
            return removed;
        }
        finally
        {
            _writeLock.Release();
        }
    }
}

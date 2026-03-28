using System;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Data.Sqlite;
using SpireOracle.UI;

namespace SpireOracle.Data;

public enum DbActionKind
{
    StartRun, EndRun,
    StartCombat, EndCombat,
    StartTurn,
    CombatAction,
    EventDecision,
    MapChoice,
    RewardDecision
}

public readonly record struct DbAction(
    DbActionKind Kind,
    string? Id1,
    string? Id2,
    int Amount,
    int ActIndex,
    int FloorIndex,
    string? Detail
);

public static class LiveRunDb
{
    private static SqliteConnection? _conn;
    private static readonly ConcurrentQueue<DbAction> _queue = new();
    private static Thread? _writerThread;
    private static volatile bool _running;
    private static readonly ManualResetEventSlim _signal = new(false);

    // State tracking — only accessed on writer thread
    private static long _currentRunId;
    private static long _currentCombatId;
    private static long _currentTurnId;
    private static int _actionSeq;

    public static bool IsInitialized => _conn != null;

    public static void Initialize(string modPath)
    {
        try
        {
            var dbPath = System.IO.Path.Combine(modPath, "spireoracle_live.db");
            _conn = new SqliteConnection($"Data Source={dbPath}");
            _conn.Open();

            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
            cmd.ExecuteNonQuery();

            using var schemaCmd = _conn.CreateCommand();
            schemaCmd.CommandText = LiveRunSchema.Sql;
            schemaCmd.ExecuteNonQuery();

            _running = true;
            _writerThread = new Thread(WriterLoop)
            {
                Name = "SpireOracle-LiveDb",
                IsBackground = true
            };
            _writerThread.Start();

            DebugLogOverlay.Log("[SpireOracle] Live capture DB initialized");
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] Live capture DB init failed: {ex.Message}");
            _conn = null;
        }
    }

    public static void Enqueue(DbAction action)
    {
        if (!_running) return;
        _queue.Enqueue(action);
        _signal.Set();
    }

    public static void Shutdown()
    {
        _running = false;
        _signal.Set();
        _writerThread?.Join(timeout: TimeSpan.FromSeconds(5));
        _conn?.Close();
        _conn?.Dispose();
        _conn = null;
    }

    private static void WriterLoop()
    {
        while (_running)
        {
            _signal.Wait(TimeSpan.FromMilliseconds(100));
            _signal.Reset();
            DrainQueue();
        }
        // Final drain on shutdown
        DrainQueue();
    }

    private static void DrainQueue()
    {
        if (_conn == null || _queue.IsEmpty) return;

        try
        {
            using var tx = _conn.BeginTransaction();
            int count = 0;
            while (_queue.TryDequeue(out var action) && count < 200)
            {
                ProcessAction(action, tx);
                count++;
            }
            tx.Commit();
        }
        catch (Exception ex)
        {
            DebugLogOverlay.LogErr($"[SpireOracle] Live DB write error: {ex.Message}");
        }
    }

    private static void ProcessAction(DbAction a, SqliteTransaction tx)
    {
        switch (a.Kind)
        {
            case DbActionKind.StartRun:
                ExecuteInsert(tx,
                    "INSERT INTO LiveRuns (Seed, Character, Ascension, StartedAt) VALUES (@p1, @p2, @p3, @p4)",
                    a.Id1 ?? "", a.Id2 ?? "", a.Amount, Now());
                _currentRunId = LastInsertRowId(tx);
                _currentCombatId = 0;
                _currentTurnId = 0;
                break;

            case DbActionKind.EndRun:
                if (_currentRunId > 0)
                {
                    if (a.Detail == "LINK_FILE")
                    {
                        // Just link the run file name, don't change Win or EndedAt
                        ExecuteUpdate(tx,
                            "UPDATE LiveRuns SET RunFileName = @p1 WHERE Id = @p2",
                            a.Id1, _currentRunId);
                        break;
                    }
                    // Id1=RunFileName, Id2=Seed, Amount=Win
                    var setSeed = !string.IsNullOrEmpty(a.Id2) ? ", Seed = @p5" : "";
                    if (!string.IsNullOrEmpty(a.Id2))
                    {
                        ExecuteUpdate(tx,
                            $"UPDATE LiveRuns SET EndedAt = @p1, Win = @p2, RunFileName = @p3, Seed = @p4 WHERE Id = @p5",
                            Now(), a.Amount, a.Id1, a.Id2, _currentRunId);
                    }
                    else
                    {
                        ExecuteUpdate(tx,
                            "UPDATE LiveRuns SET EndedAt = @p1, Win = @p2, RunFileName = @p3 WHERE Id = @p4",
                            Now(), a.Amount, a.Id1, _currentRunId);
                    }
                    _currentRunId = 0;
                }
                break;

            case DbActionKind.StartCombat:
                if (_currentRunId > 0)
                {
                    ExecuteInsert(tx,
                        "INSERT INTO Combats (RunId, EncounterId, ActIndex, FloorIndex, StartedAt) VALUES (@p1, @p2, @p3, @p4, @p5)",
                        _currentRunId, a.Id1 ?? "", a.ActIndex, a.FloorIndex, Now());
                    _currentCombatId = LastInsertRowId(tx);
                    _currentTurnId = 0;
                    _actionSeq = 0;
                }
                break;

            case DbActionKind.EndCombat:
                if (_currentCombatId > 0)
                {
                    ExecuteUpdate(tx,
                        "UPDATE Combats SET EndedAt = @p1, Won = @p2 WHERE Id = @p3",
                        Now(), a.Amount, _currentCombatId);
                    _currentCombatId = 0;
                    _currentTurnId = 0;
                }
                break;

            case DbActionKind.StartTurn:
                if (_currentCombatId > 0)
                {
                    // Amount=turnNumber, ActIndex=startingEnergy, FloorIndex=startingHp
                    ExecuteInsert(tx,
                        "INSERT INTO Turns (CombatId, TurnNumber, StartingEnergy, StartingHp, StartingBlock) VALUES (@p1, @p2, @p3, @p4, @p5)",
                        _currentCombatId, a.Amount, a.ActIndex, a.FloorIndex, 0);
                    _currentTurnId = LastInsertRowId(tx);
                    _actionSeq = 0;
                }
                break;

            case DbActionKind.CombatAction:
                if (_currentTurnId > 0)
                {
                    _actionSeq++;
                    // Detail carries the ActionType string
                    ExecuteInsert(tx,
                        "INSERT INTO CombatActions (TurnId, Seq, ActionType, SourceId, TargetId, Amount, Detail) VALUES (@p1, @p2, @p3, @p4, @p5, @p6, @p7)",
                        _currentTurnId, _actionSeq, a.Detail ?? "", a.Id1, a.Id2, a.Amount, null);
                }
                break;

            case DbActionKind.EventDecision:
                if (_currentRunId > 0)
                {
                    ExecuteInsert(tx,
                        "INSERT INTO EventDecisions (RunId, ActIndex, FloorIndex, EventId, OptionChosen, Timestamp) VALUES (@p1, @p2, @p3, @p4, @p5, @p6)",
                        _currentRunId, a.ActIndex, a.FloorIndex, a.Id1, a.Id2, Now());
                }
                break;

            case DbActionKind.MapChoice:
                if (_currentRunId > 0)
                {
                    ExecuteInsert(tx,
                        "INSERT INTO MapChoices (RunId, ActIndex, FloorIndex, MapPointType, Timestamp) VALUES (@p1, @p2, @p3, @p4, @p5)",
                        _currentRunId, a.ActIndex, a.FloorIndex, a.Id1 ?? "", Now());
                }
                break;

            case DbActionKind.RewardDecision:
                if (_currentRunId > 0)
                {
                    // Detail carries DecisionType, Id1 carries ItemId, Amount carries WasPicked
                    ExecuteInsert(tx,
                        "INSERT INTO RewardDecisions (RunId, ActIndex, FloorIndex, DecisionType, ItemId, WasPicked, Timestamp) VALUES (@p1, @p2, @p3, @p4, @p5, @p6, @p7)",
                        _currentRunId, a.ActIndex, a.FloorIndex, a.Detail ?? "", a.Id1, a.Amount, Now());
                }
                break;
        }
    }

    private static string Now() => DateTime.UtcNow.ToString("o");

    private static void ExecuteInsert(SqliteTransaction tx, string sql, params object?[] args)
    {
        using var cmd = _conn!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        for (int i = 0; i < args.Length; i++)
        {
            cmd.Parameters.AddWithValue($"@p{i + 1}", args[i] ?? DBNull.Value);
        }
        cmd.ExecuteNonQuery();
    }

    private static void ExecuteUpdate(SqliteTransaction tx, string sql, params object?[] args)
    {
        ExecuteInsert(tx, sql, args);
    }

    private static long LastInsertRowId(SqliteTransaction tx)
    {
        using var cmd = _conn!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT last_insert_rowid()";
        return (long)cmd.ExecuteScalar()!;
    }
}

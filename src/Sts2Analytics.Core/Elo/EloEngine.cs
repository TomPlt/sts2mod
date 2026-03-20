using System.Data;
using Dapper;

namespace Sts2Analytics.Core.Elo;

public class EloEngine
{
    private readonly IDbConnection _connection;

    public EloEngine(IDbConnection connection)
    {
        _connection = connection;
    }

    public void ProcessAllRuns()
    {
        var unprocessedRunIds = _connection.Query<long>("""
            SELECT r.Id FROM Runs r
            WHERE NOT EXISTS (
                SELECT 1 FROM EloHistory eh
                JOIN EloRatings er ON eh.EloRatingId = er.Id
                WHERE eh.RunId = r.Id
            )
            ORDER BY r.StartTime ASC
            """).ToList();

        foreach (var runId in unprocessedRunIds)
        {
            ProcessRun(runId);
        }
    }

    public void ProcessRun(long runId)
    {
        var run = _connection.QueryFirstOrDefault<RunInfo>(
            "SELECT Character, Win, StartTime FROM Runs WHERE Id = @RunId",
            new { RunId = runId });

        if (run is null) return;

        var choices = _connection.Query<ChoiceRow>("""
            SELECT cc.FloorId, cc.CardId, cc.WasPicked, f.ActIndex
            FROM CardChoices cc
            JOIN Floors f ON cc.FloorId = f.Id
            WHERE f.RunId = @RunId
            ORDER BY cc.FloorId
            """, new { RunId = runId }).ToList();

        if (choices.Count == 0) return;

        // Group by FloorId — each group is one card reward screen
        var groups = choices.GroupBy(c => c.FloorId).ToList();

        using var transaction = _connection.BeginTransaction();
        try
        {
            foreach (var group in groups)
            {
                var picked = group.Where(c => c.WasPicked != 0).Select(c => c.CardId).ToList();
                var skipped = group.Where(c => c.WasPicked == 0).Select(c => c.CardId).ToList();

                // Determine matchups based on pick/skip
                var matchups = new List<(string winner, string loser)>();

                if (picked.Count == 0)
                {
                    // All skipped — Skip wins vs each card
                    foreach (var cardId in skipped)
                    {
                        matchups.Add(("SKIP", cardId));
                    }
                }
                else
                {
                    // Picked card wins vs each skipped card AND vs Skip
                    foreach (var pickedCard in picked)
                    {
                        foreach (var skippedCard in skipped)
                        {
                            matchups.Add((pickedCard, skippedCard));
                        }
                        matchups.Add((pickedCard, "SKIP"));
                    }
                }

                // The "win" direction depends on run outcome
                foreach (var (winner, loser) in matchups)
                {
                    // In a winning run, the picked choice (winner) WINS
                    // In a losing run, the picked choice (winner) LOSES
                    bool winnerActuallyWins = run.Win != 0;

                    // Process for "overall" context
                    ProcessMatchup(winner, loser, run.Character, "overall", winnerActuallyWins, runId, run.StartTime, transaction);

                    // Process for per-character context
                    ProcessMatchup(winner, loser, run.Character, run.Character, winnerActuallyWins, runId, run.StartTime, transaction);
                }
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private void ProcessMatchup(string cardA, string cardB, string character, string context,
        bool aWins, long runId, string timestamp, IDbTransaction transaction)
    {
        var ratingA = GetOrCreateRating(cardA, character, context, transaction);
        var ratingB = GetOrCreateRating(cardB, character, context, transaction);

        var kA = EloCalculator.GetKFactor((int)ratingA.GamesPlayed);
        var kB = EloCalculator.GetKFactor((int)ratingB.GamesPlayed);
        var avgK = (kA + kB) / 2;

        var (newRatingA, newRatingB) = EloCalculator.UpdateRatings(
            ratingA.Rating, ratingB.Rating, aWins, avgK);

        // Update A
        _connection.Execute(
            "UPDATE EloRatings SET Rating = @Rating, GamesPlayed = @GamesPlayed WHERE Id = @Id",
            new { Rating = newRatingA, GamesPlayed = ratingA.GamesPlayed + 1, ratingA.Id },
            transaction);

        _connection.Execute("""
            INSERT INTO EloHistory (EloRatingId, RunId, RatingBefore, RatingAfter, Timestamp)
            VALUES (@EloRatingId, @RunId, @RatingBefore, @RatingAfter, @Timestamp)
            """,
            new { EloRatingId = ratingA.Id, RunId = runId, RatingBefore = ratingA.Rating, RatingAfter = newRatingA, Timestamp = timestamp },
            transaction);

        // Update B
        _connection.Execute(
            "UPDATE EloRatings SET Rating = @Rating, GamesPlayed = @GamesPlayed WHERE Id = @Id",
            new { Rating = newRatingB, GamesPlayed = ratingB.GamesPlayed + 1, ratingB.Id },
            transaction);

        _connection.Execute("""
            INSERT INTO EloHistory (EloRatingId, RunId, RatingBefore, RatingAfter, Timestamp)
            VALUES (@EloRatingId, @RunId, @RatingBefore, @RatingAfter, @Timestamp)
            """,
            new { EloRatingId = ratingB.Id, RunId = runId, RatingBefore = ratingB.Rating, RatingAfter = newRatingB, Timestamp = timestamp },
            transaction);
    }

    private RatingInfo GetOrCreateRating(string cardId, string character, string context, IDbTransaction transaction)
    {
        _connection.Execute(
            "INSERT OR IGNORE INTO EloRatings (CardId, Character, Context) VALUES (@CardId, @Character, @Context)",
            new { CardId = cardId, Character = character, Context = context },
            transaction);

        return _connection.QueryFirst<RatingInfo>(
            "SELECT Id, Rating, GamesPlayed FROM EloRatings WHERE CardId = @CardId AND Character = @Character AND Context = @Context",
            new { CardId = cardId, Character = character, Context = context },
            transaction);
    }

    private record RunInfo(string Character, long Win, string StartTime);
    private record ChoiceRow(long FloorId, string CardId, long WasPicked, long ActIndex);
    private record RatingInfo(long Id, double Rating, long GamesPlayed);
}

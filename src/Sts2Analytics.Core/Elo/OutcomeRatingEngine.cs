using System.Data;
using Dapper;

namespace Sts2Analytics.Core.Elo;

/// <summary>
/// Glicko-2 engine where matchup scores depend on run outcome.
/// Picked+Won beats Skipped (1.0/0.0). Picked+Lost loses to Skipped (0.0/1.0).
/// All-skipped+Lost is a draw vs SKIP (0.5). All-skipped+Won: SKIP wins (0.0).
/// </summary>
public class OutcomeRatingEngine
{
    private readonly IDbConnection _connection;

    public OutcomeRatingEngine(IDbConnection connection)
    {
        _connection = connection;
    }

    public void ResetAll()
    {
        _connection.Execute("DELETE FROM OutcomeGlicko2History");
        _connection.Execute("DELETE FROM OutcomeGlicko2Ratings");
    }

    public void ProcessAllRuns()
    {
        var unprocessedRunIds = _connection.Query<long>("""
            SELECT r.Id FROM Runs r
            WHERE NOT EXISTS (
                SELECT 1 FROM OutcomeGlicko2History oh
                JOIN OutcomeGlicko2Ratings orr ON oh.OutcomeGlicko2RatingId = orr.Id
                WHERE oh.RunId = r.Id
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
            "SELECT Id, Character, Win, StartTime FROM Runs WHERE Id = @RunId",
            new { RunId = runId });

        if (run is null) return;

        var choices = _connection.Query<ChoiceRow>("""
            SELECT cc.FloorId, cc.CardId, cc.WasPicked, cc.UpgradeLevel, f.ActIndex
            FROM CardChoices cc
            JOIN Floors f ON cc.FloorId = f.Id
            WHERE f.RunId = @RunId
            ORDER BY cc.FloorId
            """, new { RunId = runId }).ToList();

        if (choices.Count == 0) return;

        bool runWon = run.Win != 0;
        var groups = choices.GroupBy(c => c.FloorId).ToList();

        var matchupsByCardContext = new Dictionary<(string CardId, string Character, string Context), List<(string OpponentId, double Score)>>();

        foreach (var group in groups)
        {
            var actIndex = group.First().ActIndex;
            var picked = group.Where(c => c.WasPicked != 0).Select(c => MakeEntityId(c.CardId, c.UpgradeLevel)).ToList();
            var skipped = group.Where(c => c.WasPicked == 0).Select(c => MakeEntityId(c.CardId, c.UpgradeLevel)).ToList();

            var matchups = new List<(string cardA, string cardB, double scoreA, double scoreB)>();

            if (picked.Count == 0)
            {
                // All skipped: SKIP vs each skipped card
                foreach (var cardId in skipped)
                {
                    if (runWon)
                    {
                        // Skipping was correct — SKIP wins
                        matchups.Add(("SKIP", cardId, 1.0, 0.0));
                    }
                    else
                    {
                        // Skipping didn't help either — draw
                        matchups.Add(("SKIP", cardId, 0.5, 0.5));
                    }
                }
            }
            else
            {
                foreach (var pickedCard in picked)
                {
                    foreach (var skippedCard in skipped)
                    {
                        if (runWon)
                        {
                            // Pick validated — picked card wins
                            matchups.Add((pickedCard, skippedCard, 1.0, 0.0));
                        }
                        else
                        {
                            // Pick failed — penalize picked, but skipped is unproven
                            matchups.Add((pickedCard, skippedCard, 0.0, 0.5));
                        }
                    }

                    // Picked vs SKIP
                    if (runWon)
                    {
                        matchups.Add((pickedCard, "SKIP", 1.0, 0.0));
                    }
                    else
                    {
                        matchups.Add((pickedCard, "SKIP", 0.0, 1.0));
                    }
                }
            }

            var contexts = GetContexts(run.Character, actIndex);

            foreach (var (cardA, cardB, scoreA, scoreB) in matchups)
            {
                foreach (var (character, context) in contexts)
                {
                    var keyA = (cardA, character, context);
                    var keyB = (cardB, character, context);

                    if (!matchupsByCardContext.ContainsKey(keyA))
                        matchupsByCardContext[keyA] = [];
                    if (!matchupsByCardContext.ContainsKey(keyB))
                        matchupsByCardContext[keyB] = [];

                    matchupsByCardContext[keyA].Add((cardB, scoreA));
                    matchupsByCardContext[keyB].Add((cardA, scoreB));
                }
            }
        }

        using var transaction = _connection.BeginTransaction();
        try
        {
            foreach (var ((cardId, character, context), results) in matchupsByCardContext)
            {
                var rating = GetOrCreateRating(cardId, character, context, transaction);

                var currentRating = new Glicko2Calculator.Glicko2Rating(
                    rating.Rating, rating.RatingDeviation, rating.Volatility);

                if (rating.LastUpdatedRunId is not null)
                {
                    var missedRuns = CountRunsBetween(rating.LastUpdatedRunId.Value, run.Id, transaction);
                    for (int i = 0; i < missedRuns; i++)
                        currentRating = Glicko2Calculator.ApplyInactivityDecay(currentRating);
                }

                var opponents = new (Glicko2Calculator.Glicko2Rating Rating, double Score)[results.Count];
                for (int i = 0; i < results.Count; i++)
                {
                    var oppRating = GetOrCreateRating(results[i].OpponentId, character, context, transaction);
                    opponents[i] = (
                        new Glicko2Calculator.Glicko2Rating(oppRating.Rating, oppRating.RatingDeviation, oppRating.Volatility),
                        results[i].Score
                    );
                }

                var newRating = Glicko2Calculator.UpdateRating(currentRating, opponents);

                _connection.Execute("""
                    UPDATE OutcomeGlicko2Ratings
                    SET Rating = @Rating, RatingDeviation = @Rd, Volatility = @Vol,
                        GamesPlayed = @Games, LastUpdatedRunId = @RunId
                    WHERE Id = @Id
                    """,
                    new
                    {
                        Rating = newRating.Rating,
                        Rd = newRating.RatingDeviation,
                        Vol = newRating.Volatility,
                        Games = rating.GamesPlayed + 1,
                        RunId = run.Id,
                        rating.Id
                    },
                    transaction);

                _connection.Execute("""
                    INSERT INTO OutcomeGlicko2History
                        (OutcomeGlicko2RatingId, RunId, RatingBefore, RatingAfter, RdBefore, RdAfter,
                         VolatilityBefore, VolatilityAfter, Timestamp)
                    VALUES (@RatingId, @RunId, @RatingBefore, @RatingAfter, @RdBefore, @RdAfter,
                            @VolBefore, @VolAfter, @Timestamp)
                    """,
                    new
                    {
                        RatingId = rating.Id,
                        RunId = run.Id,
                        RatingBefore = currentRating.Rating,
                        RatingAfter = newRating.Rating,
                        RdBefore = currentRating.RatingDeviation,
                        RdAfter = newRating.RatingDeviation,
                        VolBefore = currentRating.Volatility,
                        VolAfter = newRating.Volatility,
                        Timestamp = run.StartTime
                    },
                    transaction);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static string MakeEntityId(string cardId, long upgradeLevel)
        => upgradeLevel > 0 ? $"{cardId}+{upgradeLevel}" : cardId;

    private static List<(string Character, string Context)> GetContexts(string character, long actIndex)
    {
        var actContext = $"{character}_ACT{actIndex + 1}";
        return
        [
            ("ALL", "overall"),
            (character, character),
            (character, actContext),
        ];
    }

    private int CountRunsBetween(long fromRunId, long toRunId, IDbTransaction transaction)
    {
        return _connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM Runs WHERE Id > @From AND Id < @To",
            new { From = fromRunId, To = toRunId },
            transaction);
    }

    private RatingInfo GetOrCreateRating(string cardId, string character, string context, IDbTransaction transaction)
    {
        _connection.Execute(
            "INSERT OR IGNORE INTO OutcomeGlicko2Ratings (CardId, Character, Context) VALUES (@CardId, @Character, @Context)",
            new { CardId = cardId, Character = character, Context = context },
            transaction);

        return _connection.QueryFirst<RatingInfo>("""
            SELECT Id, Rating, RatingDeviation, Volatility, GamesPlayed, LastUpdatedRunId
            FROM OutcomeGlicko2Ratings
            WHERE CardId = @CardId AND Character = @Character AND Context = @Context
            """,
            new { CardId = cardId, Character = character, Context = context },
            transaction);
    }

    private record RunInfo(long Id, string Character, long Win, string StartTime);
    private record ChoiceRow(long FloorId, string CardId, long WasPicked, long UpgradeLevel, long ActIndex);

    private record RatingInfo
    {
        public long Id { get; init; }
        public double Rating { get; init; }
        public double RatingDeviation { get; init; }
        public double Volatility { get; init; }
        public int GamesPlayed { get; init; }
        public long? LastUpdatedRunId { get; init; }
    }
}

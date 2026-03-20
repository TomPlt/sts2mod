using System.Data;
using Dapper;

namespace Sts2Analytics.Core.Elo;

public class Glicko2Engine
{
    private readonly IDbConnection _connection;

    public Glicko2Engine(IDbConnection connection)
    {
        _connection = connection;
    }

    public void ProcessAllRuns()
    {
        var unprocessedRunIds = _connection.Query<long>("""
            SELECT r.Id FROM Runs r
            WHERE NOT EXISTS (
                SELECT 1 FROM Glicko2History gh
                JOIN Glicko2Ratings gr ON gh.Glicko2RatingId = gr.Id
                WHERE gh.RunId = r.Id
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

        // Group by FloorId — each group is one card reward screen
        var groups = choices.GroupBy(c => c.FloorId).ToList();

        // Collect all matchups per card per context across the entire run (one rating period)
        var matchupsByCardContext = new Dictionary<(string CardId, string Character, string Context), List<(string OpponentId, double Score)>>();

        foreach (var group in groups)
        {
            var actIndex = group.First().ActIndex;
            var picked = group.Where(c => c.WasPicked != 0).Select(c => MakeEntityId(c.CardId, c.UpgradeLevel)).ToList();
            var skipped = group.Where(c => c.WasPicked == 0).Select(c => MakeEntityId(c.CardId, c.UpgradeLevel)).ToList();

            var matchups = new List<(string winner, string loser)>();

            if (picked.Count == 0)
            {
                foreach (var cardId in skipped)
                    matchups.Add(("SKIP", cardId));
            }
            else
            {
                foreach (var pickedCard in picked)
                {
                    foreach (var skippedCard in skipped)
                        matchups.Add((pickedCard, skippedCard));
                    matchups.Add((pickedCard, "SKIP"));
                }
            }

            var contexts = GetContexts(run.Character, actIndex);

            foreach (var (winner, loser) in matchups)
            {
                foreach (var (character, context) in contexts)
                {
                    var winnerKey = (winner, character, context);
                    var loserKey = (loser, character, context);

                    if (!matchupsByCardContext.ContainsKey(winnerKey))
                        matchupsByCardContext[winnerKey] = [];
                    if (!matchupsByCardContext.ContainsKey(loserKey))
                        matchupsByCardContext[loserKey] = [];

                    matchupsByCardContext[winnerKey].Add((loser, 1.0));
                    matchupsByCardContext[loserKey].Add((winner, 0.0));
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
                    UPDATE Glicko2Ratings
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
                    INSERT INTO Glicko2History
                        (Glicko2RatingId, RunId, RatingBefore, RatingAfter, RdBefore, RdAfter,
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
            "INSERT OR IGNORE INTO Glicko2Ratings (CardId, Character, Context) VALUES (@CardId, @Character, @Context)",
            new { CardId = cardId, Character = character, Context = context },
            transaction);

        return _connection.QueryFirst<RatingInfo>("""
            SELECT Id, Rating, RatingDeviation, Volatility, GamesPlayed, LastUpdatedRunId
            FROM Glicko2Ratings
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

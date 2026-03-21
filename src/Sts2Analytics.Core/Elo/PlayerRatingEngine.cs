using System.Data;
using Dapper;
using static Sts2Analytics.Core.Elo.Glicko2Calculator;

namespace Sts2Analytics.Core.Elo;

public class PlayerRatingEngine
{
    private readonly IDbConnection _connection;

    // Ascension opponent rating: A0=1200, A20=2400
    private const double BaseOpponentRating = 1200.0;
    private const double OpponentRatingPerAscension = 60.0;
    private const double OpponentRd = 50.0;

    public PlayerRatingEngine(IDbConnection connection) => _connection = connection;

    public void ProcessAllRuns()
    {
        var unprocessedRunIds = _connection.Query<long>("""
            SELECT r.Id FROM Runs r
            WHERE NOT EXISTS (
                SELECT 1 FROM PlayerRatingHistory ph
                JOIN PlayerRatings pr ON ph.PlayerRatingId = pr.Id
                WHERE ph.RunId = r.Id
            )
            ORDER BY r.StartTime ASC
            """).ToList();

        foreach (var runId in unprocessedRunIds)
            ProcessRun(runId);
    }

    private void ProcessRun(long runId)
    {
        var run = _connection.QueryFirstOrDefault<RunInfo>(
            "SELECT Id, Character, Ascension, Win FROM Runs WHERE Id = @RunId",
            new { RunId = runId });
        if (run is null) return;

        var opponentRating = BaseOpponentRating + run.Ascension * OpponentRatingPerAscension;
        var opponent = new Glicko2Rating(opponentRating, OpponentRd, 0.06);
        double score = run.Win == 1 ? 1.0 : 0.0;
        var opponentLabel = $"A{run.Ascension}";

        using var transaction = _connection.BeginTransaction();
        try
        {
            var contexts = new[] { run.Character, "overall" };
            foreach (var context in contexts)
            {
                var rating = GetOrCreateRating(context, transaction);

                var currentRating = new Glicko2Rating(rating.Rating, rating.RatingDeviation, rating.Volatility);
                if (rating.LastUpdatedRunId is not null)
                {
                    int missedRuns;
                    if (context == "overall")
                    {
                        missedRuns = _connection.ExecuteScalar<int>(
                            "SELECT COUNT(*) FROM Runs WHERE Id > @LastRunId AND Id < @CurrentRunId",
                            new { LastRunId = rating.LastUpdatedRunId, CurrentRunId = runId },
                            transaction);
                    }
                    else
                    {
                        missedRuns = _connection.ExecuteScalar<int>(
                            "SELECT COUNT(*) FROM Runs WHERE Id > @LastRunId AND Id < @CurrentRunId AND Character = @Character",
                            new { LastRunId = rating.LastUpdatedRunId, CurrentRunId = runId, Character = run.Character },
                            transaction);
                    }

                    for (int i = 0; i < missedRuns; i++)
                        currentRating = ApplyInactivityDecay(currentRating);
                }

                var results = new[] { (Rating: opponent, Score: score) };
                var newRating = UpdateRating(currentRating, results);

                _connection.Execute("""
                    INSERT INTO PlayerRatingHistory
                        (PlayerRatingId, RunId, RatingBefore, RatingAfter,
                         RdBefore, RdAfter, VolatilityBefore, VolatilityAfter,
                         Opponent, OpponentRating, Outcome)
                    VALUES (@PlayerRatingId, @RunId, @RatingBefore, @RatingAfter,
                            @RdBefore, @RdAfter, @VolatilityBefore, @VolatilityAfter,
                            @Opponent, @OpponentRating, @Outcome)
                    """, new {
                        PlayerRatingId = rating.Id,
                        RunId = runId,
                        RatingBefore = currentRating.Rating,
                        RatingAfter = newRating.Rating,
                        RdBefore = currentRating.RatingDeviation,
                        RdAfter = newRating.RatingDeviation,
                        VolatilityBefore = currentRating.Volatility,
                        VolatilityAfter = newRating.Volatility,
                        Opponent = opponentLabel,
                        OpponentRating = opponentRating,
                        Outcome = score
                    }, transaction);

                _connection.Execute("""
                    UPDATE PlayerRatings
                    SET Rating = @Rating, RatingDeviation = @Rd, Volatility = @Vol,
                        GamesPlayed = GamesPlayed + 1, LastUpdatedRunId = @RunId
                    WHERE Id = @Id
                    """, new {
                        Rating = newRating.Rating, Rd = newRating.RatingDeviation,
                        Vol = newRating.Volatility, RunId = runId, Id = rating.Id
                    }, transaction);
            }

            transaction.Commit();
        }
        catch { transaction.Rollback(); throw; }
    }

    private RatingInfo GetOrCreateRating(string context, IDbTransaction transaction)
    {
        var existing = _connection.QueryFirstOrDefault<RatingInfo>(
            "SELECT Id, Rating, RatingDeviation, Volatility, GamesPlayed, LastUpdatedRunId FROM PlayerRatings WHERE Context = @Context",
            new { Context = context }, transaction);

        if (existing is not null) return existing;

        _connection.Execute(
            "INSERT INTO PlayerRatings (Context) VALUES (@Context)",
            new { Context = context }, transaction);

        var id = _connection.ExecuteScalar<long>("SELECT last_insert_rowid()", transaction: transaction);
        return new RatingInfo { Id = id, Rating = 1500.0, RatingDeviation = 350.0, Volatility = 0.06, GamesPlayed = 0 };
    }

    private record RunInfo(long Id, string Character, long Ascension, long Win);
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

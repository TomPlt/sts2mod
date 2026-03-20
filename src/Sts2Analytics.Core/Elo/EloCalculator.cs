namespace Sts2Analytics.Core.Elo;

public static class EloCalculator
{
    public static double ExpectedScore(double ratingA, double ratingB)
        => 1.0 / (1.0 + Math.Pow(10, (ratingB - ratingA) / 400.0));

    public static (double newA, double newB) UpdateRatings(
        double ratingA, double ratingB, bool aWins, int kFactor = 20)
    {
        var expectedA = ExpectedScore(ratingA, ratingB);
        var scoreA = aWins ? 1.0 : 0.0;
        var newA = ratingA + kFactor * (scoreA - expectedA);
        var newB = ratingB + kFactor * ((1.0 - scoreA) - (1.0 - expectedA));
        return (newA, newB);
    }

    public static int GetKFactor(int gamesPlayed) => gamesPlayed switch
    {
        < 10 => 40,
        < 30 => 20,
        _ => 10
    };
}

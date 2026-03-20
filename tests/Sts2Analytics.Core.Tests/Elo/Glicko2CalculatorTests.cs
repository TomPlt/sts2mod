using Sts2Analytics.Core.Elo;

namespace Sts2Analytics.Core.Tests.Elo;

public class Glicko2CalculatorTests
{
    // Reference values from Glickman's paper (Example in Section 3)
    // Player: rating 1500, RD 200, vol 0.06
    // Opponents: (1400, 30, win), (1550, 100, loss), (1700, 300, loss)
    // Expected result: rating ~1464.06, RD ~151.52

    [Fact]
    public void UpdateRating_GlickmanExample_MatchesExpected()
    {
        var rating = new Glicko2Calculator.Glicko2Rating(1500, 200, 0.06);
        var opponents = new[]
        {
            (Rating: new Glicko2Calculator.Glicko2Rating(1400, 30, 0.06), Score: 1.0),
            (Rating: new Glicko2Calculator.Glicko2Rating(1550, 100, 0.06), Score: 0.0),
            (Rating: new Glicko2Calculator.Glicko2Rating(1700, 300, 0.06), Score: 0.0),
        };

        var result = Glicko2Calculator.UpdateRating(rating, opponents);

        Assert.InRange(result.Rating, 1460, 1468);
        Assert.InRange(result.RatingDeviation, 148, 155);
        Assert.True(result.Volatility > 0);
    }

    [Fact]
    public void UpdateRating_NoGames_OnlyRdIncreases()
    {
        var rating = new Glicko2Calculator.Glicko2Rating(1500, 100, 0.06);
        var result = Glicko2Calculator.ApplyInactivityDecay(rating);

        Assert.Equal(1500, result.Rating);
        Assert.True(result.RatingDeviation > 100);
        Assert.Equal(0.06, result.Volatility);
    }

    [Fact]
    public void UpdateRating_EqualOpponents_Win_RatingIncreases()
    {
        var rating = new Glicko2Calculator.Glicko2Rating(1500, 200, 0.06);
        var opponents = new[]
        {
            (Rating: new Glicko2Calculator.Glicko2Rating(1500, 200, 0.06), Score: 1.0),
        };

        var result = Glicko2Calculator.UpdateRating(rating, opponents);
        Assert.True(result.Rating > 1500);
    }

    [Fact]
    public void UpdateRating_EqualOpponents_Loss_RatingDecreases()
    {
        var rating = new Glicko2Calculator.Glicko2Rating(1500, 200, 0.06);
        var opponents = new[]
        {
            (Rating: new Glicko2Calculator.Glicko2Rating(1500, 200, 0.06), Score: 0.0),
        };

        var result = Glicko2Calculator.UpdateRating(rating, opponents);
        Assert.True(result.Rating < 1500);
    }

    [Fact]
    public void ApplyInactivityDecay_MultiplePeriodsGrowsRd()
    {
        var rating = new Glicko2Calculator.Glicko2Rating(1500, 100, 0.06);

        var decayed = rating;
        for (int i = 0; i < 5; i++)
            decayed = Glicko2Calculator.ApplyInactivityDecay(decayed);

        Assert.True(decayed.RatingDeviation > rating.RatingDeviation);
        Assert.True(decayed.RatingDeviation <= 350); // Capped at initial RD
        Assert.Equal(1500, decayed.Rating); // Rating unchanged
    }

    [Fact]
    public void UpdateRating_LowRd_SmallRatingChange()
    {
        var established = new Glicko2Calculator.Glicko2Rating(1500, 50, 0.06);
        var fresh = new Glicko2Calculator.Glicko2Rating(1500, 300, 0.06);
        var opponent = new[] { (Rating: new Glicko2Calculator.Glicko2Rating(1500, 200, 0.06), Score: 1.0) };

        var resultEstablished = Glicko2Calculator.UpdateRating(established, opponent);
        var resultFresh = Glicko2Calculator.UpdateRating(fresh, opponent);

        // Fresh rating should move more than established
        Assert.True(Math.Abs(resultFresh.Rating - 1500) > Math.Abs(resultEstablished.Rating - 1500));
    }
}

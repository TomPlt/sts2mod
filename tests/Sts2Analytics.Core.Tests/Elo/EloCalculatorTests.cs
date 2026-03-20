using Sts2Analytics.Core.Elo;

namespace Sts2Analytics.Core.Tests.Elo;

public class EloCalculatorTests
{
    [Fact]
    public void ExpectedScore_EqualRatings_Returns0_5()
    {
        var expected = EloCalculator.ExpectedScore(1500, 1500);
        Assert.Equal(0.5, expected, precision: 3);
    }

    [Fact]
    public void ExpectedScore_HigherRating_ReturnsAbove0_5()
    {
        var expected = EloCalculator.ExpectedScore(1600, 1400);
        Assert.True(expected > 0.5);
        Assert.True(expected < 1.0);
    }

    [Fact]
    public void UpdateRating_WinnerGains_LoserLoses()
    {
        var (newA, newB) = EloCalculator.UpdateRatings(1500, 1500, true, kFactor: 20);
        Assert.True(newA > 1500);
        Assert.True(newB < 1500);
        Assert.Equal(3000.0, newA + newB, precision: 1);
    }

    [Fact]
    public void GetKFactor_ScalesWithGamesPlayed()
    {
        Assert.Equal(40, EloCalculator.GetKFactor(5));
        Assert.Equal(20, EloCalculator.GetKFactor(20));
        Assert.Equal(10, EloCalculator.GetKFactor(50));
    }
}

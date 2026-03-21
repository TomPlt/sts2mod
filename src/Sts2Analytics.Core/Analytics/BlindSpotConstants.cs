namespace Sts2Analytics.Core.Analytics;

public static class BlindSpotConstants
{
    /// <summary>Divisor in logistic function controlling expected pick rate curve steepness.</summary>
    public const double LogisticDivisor = 200.0;

    /// <summary>Minimum blind spot score to flag a card.</summary>
    public const double ScoreThreshold = 0.02;

    /// <summary>Minimum times offered before a card can be flagged.</summary>
    public const int MinSampleSize = 5;

    /// <summary>Minimum confidence weight (1 - RD/350) to flag a card.</summary>
    public const double MinConfidenceWeight = 0.3;
}

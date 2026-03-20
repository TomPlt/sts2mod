namespace Sts2Analytics.Core.Elo;

public static class Glicko2Calculator
{
    // System constant — constrains volatility change per period.
    // Lower = more conservative. Range 0.3–1.2. 0.5 is reasonable default.
    private const double Tau = 0.5;
    private const double ConvergenceTolerance = 0.000001;
    private const double MaxRd = 350.0;

    // Glicko-2 scale factor: 173.7178 = 400 / ln(10)
    private const double ScaleFactor = 173.7178;

    public record Glicko2Rating(double Rating, double RatingDeviation, double Volatility);

    /// <summary>
    /// Update a player's rating after a rating period with one or more game results.
    /// </summary>
    public static Glicko2Rating UpdateRating(
        Glicko2Rating player,
        ReadOnlySpan<(Glicko2Rating Rating, double Score)> results)
    {
        if (results.Length == 0)
            return ApplyInactivityDecay(player);

        // Step 1: Convert to Glicko-2 scale
        double mu = (player.Rating - 1500) / ScaleFactor;
        double phi = player.RatingDeviation / ScaleFactor;
        double sigma = player.Volatility;

        // Convert opponents
        Span<double> muJ = stackalloc double[results.Length];
        Span<double> phiJ = stackalloc double[results.Length];
        Span<double> scores = stackalloc double[results.Length];
        for (int i = 0; i < results.Length; i++)
        {
            muJ[i] = (results[i].Rating.Rating - 1500) / ScaleFactor;
            phiJ[i] = results[i].Rating.RatingDeviation / ScaleFactor;
            scores[i] = results[i].Score;
        }

        // Step 2: Compute v (estimated variance)
        double v = 0;
        for (int i = 0; i < results.Length; i++)
        {
            double g = G(phiJ[i]);
            double e = E(mu, muJ[i], phiJ[i]);
            v += g * g * e * (1 - e);
        }
        v = 1.0 / v;

        // Step 3: Compute delta (estimated improvement)
        double delta = 0;
        for (int i = 0; i < results.Length; i++)
        {
            double g = G(phiJ[i]);
            double e = E(mu, muJ[i], phiJ[i]);
            delta += g * (scores[i] - e);
        }
        delta *= v;

        // Step 4: Determine new volatility via Illinois algorithm
        double sigmaNew = ComputeNewVolatility(sigma, phi, v, delta);

        // Step 5: Update phi using new sigma
        double phiStar = Math.Sqrt(phi * phi + sigmaNew * sigmaNew);

        // Step 6: Update phi and mu
        double phiNew = 1.0 / Math.Sqrt(1.0 / (phiStar * phiStar) + 1.0 / v);
        double muNew = mu + phiNew * phiNew * delta / v;

        // Step 7: Convert back to original scale
        double ratingNew = muNew * ScaleFactor + 1500;
        double rdNew = Math.Min(phiNew * ScaleFactor, MaxRd);

        return new Glicko2Rating(ratingNew, rdNew, sigmaNew);
    }

    /// <summary>
    /// Apply inactivity decay — RD grows when card is not seen.
    /// phi' = sqrt(phi^2 + sigma^2), capped at MaxRd.
    /// </summary>
    public static Glicko2Rating ApplyInactivityDecay(Glicko2Rating rating)
    {
        double phi = rating.RatingDeviation / ScaleFactor;
        double phiNew = Math.Sqrt(phi * phi + rating.Volatility * rating.Volatility);
        double rdNew = Math.Min(phiNew * ScaleFactor, MaxRd);
        return rating with { RatingDeviation = rdNew };
    }

    // g(phi) = 1 / sqrt(1 + 3*phi^2 / pi^2)
    private static double G(double phi)
        => 1.0 / Math.Sqrt(1.0 + 3.0 * phi * phi / (Math.PI * Math.PI));

    // E(mu, mu_j, phi_j) = 1 / (1 + exp(-g(phi_j) * (mu - mu_j)))
    private static double E(double mu, double muJ, double phiJ)
        => 1.0 / (1.0 + Math.Exp(-G(phiJ) * (mu - muJ)));

    // Illinois algorithm to find new volatility
    private static double ComputeNewVolatility(double sigma, double phi, double v, double delta)
    {
        double a = Math.Log(sigma * sigma);
        double phiSq = phi * phi;
        double deltaSq = delta * delta;

        double F(double x)
        {
            double ex = Math.Exp(x);
            double d = phiSq + v + ex;
            double part1 = ex * (deltaSq - phiSq - v - ex) / (2.0 * d * d);
            double part2 = (x - a) / (Tau * Tau);
            return part1 - part2;
        }

        // Initial bounds
        double A = a;
        double B;

        if (deltaSq > phiSq + v)
        {
            B = Math.Log(deltaSq - phiSq - v);
        }
        else
        {
            int k = 1;
            while (F(a - k * Tau) < 0)
                k++;
            B = a - k * Tau;
        }

        double fA = F(A);
        double fB = F(B);

        while (Math.Abs(B - A) > ConvergenceTolerance)
        {
            double C = A + (A - B) * fA / (fB - fA);
            double fC = F(C);

            if (fC * fB <= 0)
            {
                A = B;
                fA = fB;
            }
            else
            {
                fA /= 2.0;
            }

            B = C;
            fB = fC;
        }

        return Math.Exp(A / 2.0);
    }
}

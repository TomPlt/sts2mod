using System.CommandLine;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Sts2Analytics.Core.Analytics;
using Sts2Analytics.Core.Elo;
using Sts2Analytics.Core.Models;
using Sts2Analytics.Core.Database;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Cli.Commands;

public static class ExportCommand
{
    public static Command Create()
    {
        var dbOption = new Option<string?>("--db") { Description = "Database path" };
        var outputOption = new Option<string>("--output") { Description = "Output file path", DefaultValueFactory = _ => "./sts2analytics-export.json" };
        var modOption = new Option<bool>("--mod") { Description = "Export slimmed-down JSON for SpireOracle overlay mod" };

        var cmd = new Command("export", "Export all analytics to JSON")
        {
            dbOption, outputOption, modOption
        };

        cmd.SetAction(parseResult =>
        {
            var dbPath = parseResult.GetValue(dbOption) ?? SavePathDetector.GetDefaultDbPath();
            var output = parseResult.GetValue(outputOption) ?? "./sts2analytics-export.json";
            var isMod = parseResult.GetValue(modOption);

            if (isMod)
            {
                ExportMod(dbPath, output);
                return;
            }

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            Console.WriteLine("Computing analytics...");

            // Run summary
            var runAnalytics = new RunAnalytics(conn);
            var summary = runAnalytics.GetOverallWinRate();

            // Card analytics
            var cardAnalytics = new CardAnalytics(conn);
            var cardWinRates = cardAnalytics.GetCardWinRates();
            var cardPickRates = cardAnalytics.GetCardPickRates();

            // Relic analytics
            var relicAnalytics = new RelicAnalytics(conn);
            var relicWinRates = relicAnalytics.GetRelicWinRates();
            var relicPickRates = relicAnalytics.GetRelicPickRates();

            // Glicko-2 ratings
            var g2Count = conn.QueryFirstOrDefault<long?>("SELECT COUNT(*) FROM Glicko2Ratings") ?? 0;
            if (g2Count == 0)
            {
                Console.WriteLine("Processing Glicko-2 ratings...");
                var engine = new Glicko2Engine(conn);
                engine.ProcessAllRuns();
            }
            var g2Analytics = new Glicko2Analytics(conn);
            var glicko2Ratings = g2Analytics.GetRatings();

            // Combat analytics
            var combatAnalytics = new CombatAnalytics(conn);
            var damageByEncounter = combatAnalytics.GetDamageTakenByEncounter();

            // Path analytics
            var pathAnalytics = new PathAnalytics(conn);
            var eliteCorrelation = pathAnalytics.GetEliteCountCorrelation();
            var eliteCorrelationByAct = pathAnalytics.GetEliteCountCorrelationByAct();

            // Rest site analytics
            var restSiteAnalytics = new RestSiteAnalytics(conn);
            var restSiteDecisions = restSiteAnalytics.GetDecisionWinRates();
            var restSiteHpBuckets = restSiteAnalytics.GetDecisionsByHpBucket();
            var restSiteUpgrades = restSiteAnalytics.GetUpgradeImpact();
            var restSiteActBreakdown = restSiteAnalytics.GetActBreakdown();

            // Raw card choices for client-side filtering
            var cardChoices = conn.Query("""
                SELECT cc.CardId, cc.WasPicked, f.RunId
                FROM CardChoices cc
                JOIN Floors f ON cc.FloorId = f.Id
                """).Select(r => new
            {
                cardId = (string)r.CardId,
                wasPicked = (long)r.WasPicked != 0,
                runId = (long)r.RunId
            }).ToList();

            // Raw relic choices for client-side filtering
            var relicChoices = conn.Query("""
                SELECT rc.RelicId, rc.WasPicked, f.RunId
                FROM RelicChoices rc
                JOIN Floors f ON rc.FloorId = f.Id
                """).Select(r => new
            {
                relicId = (string)r.RelicId,
                wasPicked = (long)r.WasPicked != 0,
                runId = (long)r.RunId
            }).ToList();

            // Process player ratings
            var playerEngine = new PlayerRatingEngine(conn);
            playerEngine.ProcessAllRuns();

            var playerRatingData = conn.Query(
                "SELECT Context, Rating, RatingDeviation, GamesPlayed FROM PlayerRatings")
                .ToList();

            // Player rating history
            var playerHistoryData = conn.Query("""
                SELECT pr.Context, ph.RunId, ph.RatingBefore, ph.RatingAfter,
                       ph.Opponent, ph.Outcome
                FROM PlayerRatingHistory ph
                JOIN PlayerRatings pr ON ph.PlayerRatingId = pr.Id
                ORDER BY ph.RunId DESC
                """).ToList();

            // Compute blind spots
            var blindSpotAnalyzer = new BlindSpotAnalyzer(conn);
            blindSpotAnalyzer.AnalyzeAllContexts();

            var blindSpotExportData = conn.Query(
                "SELECT CardId, Context, BlindSpotType, Score, PickRate, ExpectedPickRate, WinRateDelta, GamesAnalyzed FROM BlindSpots")
                .ToList();

            // Process ancient choice ratings
            var ancientEngine = new AncientRatingEngine(conn);
            ancientEngine.ProcessAllRuns();

            var ancientRatingExport = conn.Query(
                "SELECT ChoiceKey, Character, Context, Rating, RatingDeviation, Volatility, GamesPlayed FROM AncientGlicko2Ratings")
                .ToList();

            // Combat Glicko-2 ratings
            var combatRatingCount = conn.QueryFirstOrDefault<long?>("SELECT COUNT(*) FROM CombatGlicko2Ratings") ?? 0;
            if (combatRatingCount == 0)
            {
                Console.WriteLine("Processing combat ratings...");
                var combatRatingEngine = new CombatRatingEngine(conn);
                combatRatingEngine.ProcessAllRuns();
            }
            var combatGlicko2 = new CombatGlicko2Analytics(conn);
            var combatRatings = combatGlicko2.GetRatings();
            var encounterRatings = combatGlicko2.GetEncounterRatings();
            var poolRatings = combatGlicko2.GetPoolRatings();

            // Runs list
            var runs = conn.Query("SELECT Id, Character, Win, Ascension, Seed, GameMode, Source FROM Runs ORDER BY Id").ToList();
            var runsList = runs.Select(r => new
            {
                id = (long)r.Id,
                character = (string)r.Character,
                win = (long)r.Win != 0,
                ascension = (long)r.Ascension,
                seed = (string)r.Seed,
                gameMode = (string)r.GameMode,
                source = (string)r.Source
            }).ToList();

            var exportData = new
            {
                summary = new
                {
                    summary.TotalRuns,
                    summary.Wins,
                    summary.Losses,
                    summary.WinRate,
                    summary.RunsByCharacter
                },
                cardWinRates,
                cardPickRates,
                relicWinRates,
                relicPickRates,
                glicko2Ratings,
                cardChoices,
                relicChoices,
                runs = runsList,
                damageByEncounter,
                eliteCorrelation,
                eliteCorrelationByAct,
                playerRatings = playerRatingData,
                playerRatingHistory = playerHistoryData,
                blindSpots = blindSpotExportData,
                ancientRatings = ancientRatingExport,
                restSiteDecisions,
                restSiteHpBuckets,
                restSiteUpgrades,
                restSiteActBreakdown,
                combatRatings,
                encounterRatings,
                poolRatings
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(exportData, options);
            File.WriteAllText(output, json);

            Console.WriteLine($"Exported to: {Path.GetFullPath(output)}");
            Console.WriteLine($"  {cardWinRates.Count} cards, {relicWinRates.Count} relics, {glicko2Ratings.Count} ratings, {runs.Count} runs");
        });

        return cmd;
    }

    private static void ExportMod(string dbPath, string output)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        Schema.Migrate(conn);

        Console.WriteLine("Exporting mod overlay data...");

        // Ensure Glicko-2 ratings exist
        var g2Count = conn.QueryFirstOrDefault<long?>("SELECT COUNT(*) FROM Glicko2Ratings") ?? 0;
        if (g2Count == 0)
        {
            Console.WriteLine("Processing Glicko-2 ratings...");
            var engine = new Glicko2Engine(conn);
            engine.ProcessAllRuns();
        }

        // Process player ratings
        var playerEngine = new PlayerRatingEngine(conn);
        playerEngine.ProcessAllRuns();

        // Compute blind spots
        var blindSpotAnalyzer = new BlindSpotAnalyzer(conn);
        blindSpotAnalyzer.AnalyzeAllContexts();

        // Process ancient choice ratings
        var ancientEngine = new AncientRatingEngine(conn);
        ancientEngine.ProcessAllRuns();

        // Process combat ratings
        var combatEngine = new CombatRatingEngine(conn);
        combatEngine.ProcessAllRuns();
        var combatAnalytics = new CombatGlicko2Analytics(conn);
        var allCombatRatings = combatAnalytics.GetRatings();
        var combatOverall = allCombatRatings
            .Where(r => r.Context == "overall" && r.Character == "ALL" && !r.CardId.StartsWith("POOL."))
            .ToDictionary(r => r.CardId);
        var combatByPool = allCombatRatings
            .Where(r => r.Context != "overall" && r.Character == "ALL"
                && !r.CardId.StartsWith("POOL.") && !r.CardId.StartsWith("ENC."))
            .ToLookup(r => r.CardId);

        // Per-character overall combat Elo (CHARACTER.X → rating for that character's decks)
        var combatByChar = allCombatRatings
            .Where(r => r.Context == "overall" && r.Character != "ALL"
                && !r.CardId.StartsWith("POOL.") && !r.CardId.StartsWith("ENC."))
            .ToLookup(r => r.CardId);

        // Per-context normalization: for each context, shift so the midpoint between
        // average card rating and pool rating is 1500. This centers the scale per context.
        var contextOffsets = new Dictionary<string, double>();
        foreach (var contextGroup in allCombatRatings.GroupBy(r => r.Context))
        {
            var ctx = contextGroup.Key;
            var ctxCards = contextGroup.Where(r => !r.CardId.StartsWith("POOL.") && !r.CardId.StartsWith("ENC.")).ToList();
            var ctxPools = contextGroup.Where(r => r.CardId.StartsWith("POOL.") || r.CardId.StartsWith("ENC.")).ToList();
            if (ctxCards.Count == 0 || ctxPools.Count == 0) continue;
            var midpoint = (ctxCards.Average(r => r.Rating) + ctxPools.Average(r => r.Rating)) / 2.0;
            contextOffsets[ctx] = 1500.0 - midpoint;
        }
        // Fallback: overall context offset
        var combatEloOffset = contextOffsets.GetValueOrDefault("overall", 0);
        Console.WriteLine($"  Combat Elo normalization: {contextOffsets.Count} contexts, overall offset={combatEloOffset:+0}");

        // Pool entity ratings for export (per-context normalized)
        var poolRatings = combatAnalytics.GetPoolRatings()
            .Where(r => r.Character == "ALL")
            .GroupBy(r => r.Context)
            .ToDictionary(g => g.Key, g => new PoolRating(
                g.First().Rating + contextOffsets.GetValueOrDefault(g.Key, combatEloOffset),
                g.First().RatingDeviation));

        // Net damage distributions per pool (sorted, for percentile lookup in mod)
        var damageDistributions = combatEngine.PrecomputeDamageDistributions();

        // Per-encounter ratings for export (ENC.ENCOUNTER.X -> ENCOUNTER.X as key)
        var encounterRatings = combatAnalytics.GetEncounterRatings()
            .Where(r => r.Character == "ALL" && r.Context == "overall")
            .ToDictionary(
                r => r.CardId.StartsWith("ENC.") ? r.CardId[4..] : r.CardId,
                r => new PoolRating(r.Rating + combatEloOffset, r.RatingDeviation));

        // Query Skip rating
        var skipElo = conn.QueryFirstOrDefault<double?>(
            "SELECT Rating FROM Glicko2Ratings WHERE CardId = 'SKIP' AND Character = 'ALL' AND Context = 'overall'")
            ?? 1500.0;

        // Get analytics data
        var cardAnalytics = new CardAnalytics(conn);
        var cardWinRates = cardAnalytics.GetCardWinRates()
            .ToDictionary(c => c.CardId);
        var cardPickRates = cardAnalytics.GetCardPickRates()
            .ToDictionary(c => c.CardId);

        var g2Analytics = new Glicko2Analytics(conn);
        var allRatings = g2Analytics.GetRatings();
        var eloRatings = allRatings
            .Where(e => e.Context == "overall")
            .ToDictionary(e => e.CardId);

        // Build per-act rating lookup: (cardId, actContext) -> rating
        var actRatings = allRatings
            .Where(e => e.Context.Contains("_ACT"))
            .ToLookup(e => e.CardId);

        // Skip Elo per act and per character
        var skipEloByAct = new Dictionary<string, double>();
        foreach (var r in allRatings.Where(e => e.CardId == "SKIP"))
        {
            if (r.Context == "overall") continue;
            // Per-character: context = "CHARACTER.IRONCLAD", key = "ironclad"
            // Per-character-act: context = "CHARACTER.IRONCLAD_ACT1", key = "ironclad_act1"
            var key = r.Context.Replace("CHARACTER.", "").ToLower();
            skipEloByAct[key] = r.Rating;
        }

        var blindSpots = conn.Query(
            "SELECT CardId, BlindSpotType, Score, PickRate, WinRateDelta FROM BlindSpots WHERE Context = 'overall'")
            .ToDictionary(b => (string)b.CardId);

        // Collect all card IDs (including cards with combat Elo but no pick data, e.g. starter cards)
        var allCardIds = cardWinRates.Keys
            .Union(cardPickRates.Keys)
            .Union(eloRatings.Keys)
            .Union(combatOverall.Keys)
            .Where(id => id != "SKIP")
            .OrderBy(id => id)
            .ToList();

        var cards = allCardIds.Select(id =>
        {
            var elo = eloRatings.TryGetValue(id, out var e) ? e.Rating : 0.0;
            var rd = e?.RatingDeviation ?? 350.0;
            var pickRate = cardPickRates.TryGetValue(id, out var p) ? p.PickRate : 0.0;
            var winPicked = cardWinRates.TryGetValue(id, out var w) ? w.WinRateWhenPicked : 0.0;
            var winSkipped = w?.WinRateWhenSkipped ?? 0.0;
            var delta = w?.WinRateDelta ?? 0.0;

            // Per-act ratings for this card (any character context with _ACT suffix)
            var cardActRatings = actRatings[id].ToList();
            double act1 = 0, rdAct1 = 350, act2 = 0, rdAct2 = 350, act3 = 0, rdAct3 = 350;
            foreach (var ar in cardActRatings)
            {
                if (ar.Context.EndsWith("_ACT1")) { if (ar.Rating > act1) { act1 = ar.Rating; rdAct1 = ar.RatingDeviation; } }
                else if (ar.Context.EndsWith("_ACT2")) { if (ar.Rating > act2) { act2 = ar.Rating; rdAct2 = ar.RatingDeviation; } }
                else if (ar.Context.EndsWith("_ACT3")) { if (ar.Rating > act3) { act3 = ar.Rating; rdAct3 = ar.RatingDeviation; } }
            }

            string? blindSpotType = null;
            double bsScore = 0, bsPickRate = 0, bsWinDelta = 0;
            if (blindSpots.TryGetValue(id, out var bs))
            {
                blindSpotType = (string)bs.BlindSpotType;
                bsScore = (double)bs.Score;
                bsPickRate = (double)bs.PickRate;
                bsWinDelta = (double)bs.WinRateDelta;
            }

            var combatElo = combatOverall.TryGetValue(id, out var ce) ? ce.Rating + combatEloOffset : 0.0;
            var combatRdVal = ce?.RatingDeviation ?? 350.0;
            var cardCombatPools = combatByPool[id]
                .ToDictionary(r => r.Context, r => new PoolRating(
                    r.Rating + contextOffsets.GetValueOrDefault(r.Context, combatEloOffset),
                    r.RatingDeviation));
            var cardCombatChars = combatByChar[id]
                .ToDictionary(r => r.Character.Replace("CHARACTER.", "").ToLower(),
                    r => new PoolRating(r.Rating + combatEloOffset, r.RatingDeviation));

            return new ModCardStats(id, elo, rd, pickRate, winPicked, winSkipped, delta, act1, rdAct1, act2, rdAct2, act3, rdAct3,
                blindSpotType, bsScore, bsPickRate, bsWinDelta,
                CombatElo: combatElo, CombatRd: combatRdVal,
                CombatByPool: cardCombatPools.Count > 0 ? cardCombatPools : null,
                CombatByChar: cardCombatChars.Count > 0 ? cardCombatChars : null);
        }).ToList();

        // Build ancient stats for mod export
        var ancientRatings = conn.Query(
            "SELECT ChoiceKey, Character, Context, Rating, RatingDeviation, GamesPlayed FROM AncientGlicko2Ratings")
            .ToList();

        var ancientPickRates = conn.Query("""
            SELECT ac.TextKey,
                   COUNT(*) as TimesOffered,
                   SUM(ac.WasChosen) as TimesPicked
            FROM AncientChoices ac
            GROUP BY ac.TextKey
            """).ToDictionary(r => (string)r.TextKey, r => new {
                TimesOffered = (long)r.TimesOffered,
                TimesPicked = (long)r.TimesPicked
            });

        // Per-character ratings keyed by choiceKey
        // Use "neow" context for Neow choices, fall back to any available context
        var charRatings = ancientRatings
            .Where(r => (string)r.Character != "ALL")
            .GroupBy(r => ((string)r.ChoiceKey, ((string)r.Character).Replace("CHARACTER.", "").ToLower()))
            .Select(g => {
                // Prefer neow context, then post_act1, then any
                var best = g.FirstOrDefault(r => (string)r.Context == "neow")
                    ?? g.FirstOrDefault(r => (string)r.Context == "post_act1")
                    ?? g.First();
                return new { ChoiceKey = g.Key.Item1, CharKey = g.Key.Item2,
                    Rating = (double)best.Rating, Rd = (double)best.RatingDeviation,
                    Games = (int)(long)best.GamesPlayed };
            })
            .GroupBy(r => r.ChoiceKey)
            .ToDictionary(g => g.Key, g => g.ToDictionary(
                r => r.CharKey,
                r => new AncientCharRating(r.Rating, r.Rd, r.Games)));

        var ancientByKey = ancientRatings
            .Where(r => (string)r.Character == "ALL")
            .GroupBy(r => (string)r.ChoiceKey).ToList();

        var ancientStats = ancientByKey.Select(g =>
        {
            var key = g.Key;
            double rating = 0, rd = 350;
            double rNeow = 0, rdNeow = 350;
            double rPost1 = 0, rdPost1 = 350;
            double rPost2 = 0, rdPost2 = 350;

            foreach (var r in g)
            {
                var ctx = (string)r.Context;
                if (ctx == "overall") { rating = (double)r.Rating; rd = (double)r.RatingDeviation; }
                else if (ctx == "neow") { rNeow = (double)r.Rating; rdNeow = (double)r.RatingDeviation; }
                else if (ctx == "post_act1") { rPost1 = (double)r.Rating; rdPost1 = (double)r.RatingDeviation; }
                else if (ctx == "post_act2") { rPost2 = (double)r.Rating; rdPost2 = (double)r.RatingDeviation; }
            }

            double pickRate = 0;
            int games = 0;
            if (ancientPickRates.TryGetValue(key, out var pr))
            {
                pickRate = pr.TimesOffered > 0 ? (double)pr.TimesPicked / pr.TimesOffered : 0;
                games = (int)pr.TimesOffered;
            }

            var byChar = charRatings.TryGetValue(key, out var cr) && cr.Count > 0 ? cr : null;

            return new ModAncientStats(key, rating, rd, rNeow, rdNeow, rPost1, rdPost1, rPost2, rdPost2, pickRate, games, byChar);
        }).ToList();

        // Build map intel data per character per act
        var mapIntelSql = """
            SELECT
                r.Character,
                f.ActIndex,
                CASE
                    WHEN f.EncounterId LIKE '%\_WEAK' ESCAPE '\' THEN 'weak'
                    WHEN f.EncounterId LIKE '%\_NORMAL' ESCAPE '\' THEN 'normal'
                    WHEN f.EncounterId LIKE '%\_ELITE' ESCAPE '\' THEN 'elite'
                    WHEN f.EncounterId LIKE '%\_BOSS' ESCAPE '\' THEN 'boss'
                    ELSE NULL
                END AS Pool,
                f.EncounterId,
                f.DamageTaken
            FROM Floors f
            JOIN Runs r ON f.RunId = r.Id
            WHERE f.EncounterId IS NOT NULL
              AND f.MapPointType IN ('monster', 'elite', 'boss')
            """;

        var mapIntelRows = conn.Query(mapIntelSql).ToList();

        // Per-character win rates
        var charWinRates = conn.Query("""
            SELECT Character, COUNT(*) as Runs, SUM(CASE WHEN Win = 1 THEN 1 ELSE 0 END) as Wins
            FROM Runs GROUP BY Character
            """).ToDictionary(r => (string)r.Character, r => new { Runs = (int)(long)r.Runs, Wins = (int)(long)r.Wins });

        // Per-character per-act elite count and survival rate
        // A run "survived" an act if it reached a higher act, OR won the game
        // First get max act reached per run
        var maxActPerRun = conn.Query("""
            SELECT RunId, MAX(ActIndex) as MaxActReached FROM Floors GROUP BY RunId
            """).ToDictionary(r => (long)r.RunId, r => (int)(long)r.MaxActReached);

        var perActStats = conn.Query("""
            SELECT r.Character, r.Win, r.Id as RunId,
                   f.ActIndex,
                   SUM(CASE WHEN f.MapPointType = 'elite' THEN 1 ELSE 0 END) as EliteCount
            FROM Runs r
            JOIN Floors f ON f.RunId = r.Id
            GROUP BY r.Id, f.ActIndex
            """).Select(r => {
                var runId = (long)r.RunId;
                var maxAct = maxActPerRun.TryGetValue(runId, out var ma) ? ma : 0;
                return new { r.Character, r.Win, r.RunId, r.ActIndex, r.EliteCount, MaxAct = (long)maxAct };
            }).ToList();

        // Key: (character, actIndex) -> list of (runId, win, eliteCount, maxAct)
        var actRunStats = perActStats
            .GroupBy(r => ((string)r.Character, (int)(long)r.ActIndex))
            .ToDictionary(g => g.Key, g => g.ToList());

        var mapIntel = mapIntelRows
            .Where(r => r.Pool != null)
            .GroupBy(r => (string)r.Character)
            .Select(charGroup =>
            {
                var charKey = charGroup.Key;
                var wr = charWinRates.TryGetValue(charKey, out var w) ? w : null;

                return new MapIntelCharacter(
                    charKey,
                    wr?.Runs ?? 0,
                    wr?.Wins ?? 0,
                    wr != null && wr.Runs > 0 ? (double)wr.Wins / wr.Runs : 0,
                    charGroup
                        .GroupBy(r => (int)(long)r.ActIndex)
                        .OrderBy(g => g.Key)
                        .Select(actGroup =>
                        {
                            var actIdx = actGroup.Key;
                            var actRuns = actRunStats.TryGetValue((charKey, actIdx), out var ar) ? ar : null;
                            var actRunCount = actRuns?.Count ?? 0;
                            // "Survived act" = reached a higher act OR won the run
                            var actWinCount = actRuns?.Count(r => r.MaxAct > actIdx || (long)r.Win == 1) ?? 0;
                            var actWinRate = actRunCount > 0 ? (double)actWinCount / actRunCount : 0;

                            // Elite correlation for this act only
                            var eliteCorr = actRuns?
                                .GroupBy(r => (int)(long)r.EliteCount)
                                .Select(eg => new EliteCorrelation(
                                    eg.Key,
                                    eg.Count(),
                                    eg.Count(r => (long)r.Win == 1),
                                    eg.Count() > 0 ? (double)eg.Count(r => (long)r.Win == 1) / eg.Count() : 0))
                                .OrderBy(e => e.EliteCount)
                                .ToList();

                            return new MapIntelAct(
                                actIdx,
                                actGroup
                                    .GroupBy(r => (string)r.Pool)
                                    .Select(poolGroup => {
                                        var dmgValues = poolGroup.Select(r => (double)(long)r.DamageTaken).ToList();
                                        return new MapIntelPool(
                                            poolGroup.Key,
                                            dmgValues.Average(),
                                            StdDev(dmgValues),
                                            dmgValues.Count,
                                            poolGroup.Select(r => (string)r.EncounterId).Distinct().OrderBy(e2 => e2).ToList(),
                                            poolGroup.GroupBy(r => (string)r.EncounterId)
                                                .Select(eg => {
                                                    var eDmg = eg.Select(r => (double)(long)r.DamageTaken).ToList();
                                                    return new EncounterDamage(
                                                        eg.Key,
                                                        eDmg.Average(),
                                                        StdDev(eDmg),
                                                        eDmg.Count,
                                                        (int)eDmg.Max());
                                                })
                                                .OrderByDescending(ed => ed.AvgDamage)
                                                .ToList());
                                    })
                                    .OrderBy(p => p.Pool switch { "weak" => 0, "normal" => 1, "elite" => 2, "boss" => 3, _ => 4 })
                                    .ToList(),
                                actRunCount, actWinCount, actWinRate, eliteCorr);
                        })
                        .ToList());
            })
            .ToList();

        // Player run counts by source
        var playerRunCounts = conn.Query("""
            SELECT Source, COUNT(*) as Runs, SUM(CASE WHEN Win = 1 THEN 1 ELSE 0 END) as Wins
            FROM Runs WHERE Source != '' GROUP BY Source ORDER BY Runs DESC
            """)
            .Select(r => new PlayerRunCount(
                (string)r.Source,
                (int)(long)r.Runs,
                (int)(long)r.Wins,
                (long)r.Runs > 0 ? (double)(long)r.Wins / (long)r.Runs : 0))
            .ToList();

        var overlayData = new ModOverlayData(
            Version: 5,
            ExportedAt: DateTime.UtcNow.ToString("o"),
            SkipElo: skipElo,
            SkipEloByAct: skipEloByAct,
            Cards: cards,
            AncientChoices: ancientStats,
            MapIntel: mapIntel,
            EncounterPools: poolRatings,
            EncounterRatings: encounterRatings,
            DamageDistributions: damageDistributions,
            PlayerRunCounts: playerRunCounts.Count > 0 ? playerRunCounts : null);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var json = JsonSerializer.Serialize(overlayData, options);
        File.WriteAllText(output, json);

        Console.WriteLine($"Mod overlay data exported to: {Path.GetFullPath(output)}");
        Console.WriteLine($"  {cards.Count} cards, skipElo: {skipElo:F1}");
    }

    private static double StdDev(IEnumerable<double> values)
    {
        var list = values.ToList();
        if (list.Count < 2) return 0;
        var avg = list.Average();
        var sumSq = list.Sum(v => (v - avg) * (v - avg));
        return Math.Sqrt(sumSq / list.Count);
    }
}

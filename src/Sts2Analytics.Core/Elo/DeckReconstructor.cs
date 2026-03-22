using System.Data;
using Dapper;

namespace Sts2Analytics.Core.Elo;

public class DeckReconstructor
{
    private readonly IDbConnection _connection;

    public DeckReconstructor(IDbConnection connection)
    {
        _connection = connection;
    }

    /// <summary>
    /// Returns the list of card entity IDs (e.g. "CARD.STRIKE", "CARD.INFLAME+1")
    /// that were in the deck at the point when the given floor was reached.
    /// </summary>
    public List<string> GetDeckAtFloor(long runId, long floorId)
    {
        // Build events from all 4 sources, then sort and replay

        var events = new List<(long FloorDbId, string Action, string EntityId)>();

        // 1. CardsGained → add
        var cardsGained = _connection.Query<CardsGainedRow>("""
            SELECT f.Id AS FloorDbId, cg.CardId, cg.UpgradeLevel
            FROM CardsGained cg
            JOIN Floors f ON cg.FloorId = f.Id
            WHERE f.RunId = @RunId AND f.Id <= @FloorId
            """, new { RunId = runId, FloorId = floorId }).ToList();

        foreach (var cg in cardsGained)
        {
            events.Add((cg.FloorDbId, "add", MakeEntityId(cg.CardId, (int)cg.UpgradeLevel)));
        }

        // Build a lookup: (cardId, floorIndex) → upgradeLevel from CardsGained
        // Used to resolve upgrade level for CardRemovals
        var gainedByCardAndFloor = _connection.Query<CardsGainedWithFloorIndex>("""
            SELECT cg.CardId, f.FloorIndex, cg.UpgradeLevel
            FROM CardsGained cg
            JOIN Floors f ON cg.FloorId = f.Id
            WHERE f.RunId = @RunId
            ORDER BY f.Id ASC
            """, new { RunId = runId }).ToList();

        // 2. CardRemovals → remove (lookup upgrade level from CardsGained via FloorAddedToDeck)
        //    FloorAddedToDeck is the FloorIndex where the card was originally added.
        var cardRemovals = _connection.Query<CardRemovalRow>("""
            SELECT f.Id AS FloorDbId, cr.CardId, cr.FloorAddedToDeck
            FROM CardRemovals cr
            JOIN Floors f ON cr.FloorId = f.Id
            WHERE f.RunId = @RunId AND f.Id <= @FloorId
            """, new { RunId = runId, FloorId = floorId }).ToList();

        foreach (var cr in cardRemovals)
        {
            int upgradeLevel = 0;
            if (cr.FloorAddedToDeck.HasValue)
            {
                // Find the matching CardsGained entry for this card at the floor it was added
                var match = gainedByCardAndFloor.FirstOrDefault(
                    g => g.CardId == cr.CardId && g.FloorIndex == cr.FloorAddedToDeck.Value);
                if (match != null)
                    upgradeLevel = (int)match.UpgradeLevel;
            }
            else
            {
                // No floor info; find first matching gained entry
                var match = gainedByCardAndFloor.FirstOrDefault(g => g.CardId == cr.CardId);
                if (match != null)
                    upgradeLevel = (int)match.UpgradeLevel;
            }
            events.Add((cr.FloorDbId, "remove", MakeEntityId(cr.CardId, upgradeLevel)));
        }

        // 3. CardTransforms → remove original + add final
        var cardTransforms = _connection.Query<CardTransformRow>("""
            SELECT f.Id AS FloorDbId, ct.OriginalCardId, ct.FinalCardId
            FROM CardTransforms ct
            JOIN Floors f ON ct.FloorId = f.Id
            WHERE f.RunId = @RunId AND f.Id <= @FloorId
            """, new { RunId = runId, FloorId = floorId }).ToList();

        foreach (var ct in cardTransforms)
        {
            // Remove original (upgrade level unknown from schema; remove first matching base card)
            events.Add((ct.FloorDbId, "remove_by_base", ct.OriginalCardId));
            // Add final (no upgrade level in CardTransforms schema)
            events.Add((ct.FloorDbId, "add", MakeEntityId(ct.FinalCardId, 0)));
        }

        // 4. RestSiteUpgrades → upgrade CARD.X to CARD.X+1 in place
        //    Join RestSiteUpgrades → RestSiteChoices → Floors
        var restSiteUpgrades = _connection.Query<RestSiteUpgradeRow>("""
            SELECT f.Id AS FloorDbId, rsu.CardId
            FROM RestSiteUpgrades rsu
            JOIN RestSiteChoices rsc ON rsu.RestSiteChoiceId = rsc.Id
            JOIN Floors f ON rsc.FloorId = f.Id
            WHERE f.RunId = @RunId AND f.Id <= @FloorId
            """, new { RunId = runId, FloorId = floorId }).ToList();

        foreach (var rsu in restSiteUpgrades)
        {
            // CardId stored in RestSiteUpgrades is the base card id (before upgrade)
            events.Add((rsu.FloorDbId, "remove_and_upgrade", rsu.CardId));
        }

        // Sort all events by floor db id (chronological order)
        events.Sort((a, b) => a.FloorDbId.CompareTo(b.FloorDbId));

        // Replay events to build deck
        var deck = new List<string>();

        foreach (var (_, action, entityId) in events)
        {
            switch (action)
            {
                case "add":
                    deck.Add(entityId);
                    break;

                case "remove":
                    // Remove first matching instance
                    var idx = deck.IndexOf(entityId);
                    if (idx >= 0)
                        deck.RemoveAt(idx);
                    break;

                case "remove_by_base":
                    // Remove first card whose base id (without upgrade suffix) matches
                    var baseIdx = deck.FindIndex(c => GetBaseId(c) == entityId);
                    if (baseIdx >= 0)
                        deck.RemoveAt(baseIdx);
                    break;

                case "remove_and_upgrade":
                {
                    // Find the first version of this card and upgrade it by 1
                    var upgradeIdx = deck.FindIndex(c => GetBaseId(c) == entityId);
                    if (upgradeIdx >= 0)
                    {
                        var existing = deck[upgradeIdx];
                        var currentLevel = GetUpgradeLevel(existing);
                        deck[upgradeIdx] = MakeEntityId(entityId, currentLevel + 1);
                    }
                    break;
                }
            }
        }

        return deck;
    }

    public static string MakeEntityId(string cardId, int upgradeLevel)
        => upgradeLevel > 0 ? $"{cardId}+{upgradeLevel}" : cardId;

    private static string GetBaseId(string entityId)
    {
        var plusIdx = entityId.LastIndexOf('+');
        return plusIdx >= 0 ? entityId[..plusIdx] : entityId;
    }

    private static int GetUpgradeLevel(string entityId)
    {
        var plusIdx = entityId.LastIndexOf('+');
        if (plusIdx >= 0 && int.TryParse(entityId[(plusIdx + 1)..], out var level))
            return level;
        return 0;
    }

    private record CardsGainedRow(long FloorDbId, string CardId, long UpgradeLevel);
    private record CardsGainedWithFloorIndex(string CardId, long FloorIndex, long UpgradeLevel);
    private record CardRemovalRow(long FloorDbId, string CardId, long? FloorAddedToDeck);
    private record CardTransformRow(long FloorDbId, string OriginalCardId, string FinalCardId);
    private record RestSiteUpgradeRow(long FloorDbId, string CardId);
}

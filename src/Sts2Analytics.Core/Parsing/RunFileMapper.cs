using System.Text.Json;
using Sts2Analytics.Core.Models;

namespace Sts2Analytics.Core.Parsing;

public record FloorData(
    List<CardChoiceEntity> CardChoices,
    List<RelicChoiceEntity> RelicChoices,
    List<PotionChoiceEntity> PotionChoices,
    List<PotionEventEntity> PotionEvents,
    List<EventChoiceEntity> EventChoices,
    List<RestSiteChoiceEntity> RestSiteChoices,
    List<string> RestSiteUpgradedCards,
    List<CardTransformEntity> CardTransforms,
    List<MonsterEntity> Monsters,
    List<CardsGainedEntity> CardsGained,
    List<CardRemovalEntity> CardRemovals,
    List<CardEnchantmentEntity> CardEnchantments,
    List<AncientChoiceEntity> AncientChoices
);

public static class RunFileMapper
{
    public static (RunEntity Run, List<FloorEntity> Floors, List<FloorData> FloorData) Map(
        RunFile runFile, string fileName)
    {
        var player = runFile.Players.Count > 0 ? runFile.Players[0] : new PlayerData();

        var run = new RunEntity
        {
            FileName = fileName,
            Seed = runFile.Seed,
            Character = player.Character,
            Ascension = runFile.Ascension,
            GameMode = runFile.GameMode,
            BuildVersion = runFile.BuildId,
            Win = runFile.Win,
            WasAbandoned = runFile.WasAbandoned,
            KilledByEncounter = runFile.KilledByEncounter ?? "",
            KilledByEvent = runFile.KilledByEvent ?? "",
            StartTime = runFile.StartTime.ToString(),
            RunTime = runFile.RunTime,
            Acts = runFile.Acts.Count,
            SchemaVersion = runFile.SchemaVersion,
            PlatformType = runFile.PlatformType,
            Modifiers = runFile.Modifiers.Count > 0
                ? JsonSerializer.Serialize(runFile.Modifiers)
                : null,
            MaxPotionSlots = player.MaxPotionSlotCount
        };

        var floors = new List<FloorEntity>();
        var floorDataList = new List<FloorData>();

        for (int actIndex = 0; actIndex < runFile.MapPointHistory.Count; actIndex++)
        {
            var act = runFile.MapPointHistory[actIndex];
            for (int floorIndex = 0; floorIndex < act.Count; floorIndex++)
            {
                var mapPoint = act[floorIndex];
                var stats = mapPoint.PlayerStats.Count > 0 ? mapPoint.PlayerStats[0] : null;
                var room = mapPoint.Rooms.Count > 0 ? mapPoint.Rooms[0] : null;

                var floor = new FloorEntity
                {
                    ActIndex = actIndex,
                    FloorIndex = floorIndex,
                    MapPointType = mapPoint.MapPointType,
                    EncounterId = room?.ModelId,
                    RoomType = room?.RoomType,
                    TurnsTaken = room?.TurnsTaken ?? 0,
                    PlayerId = stats?.PlayerId ?? 0,
                    CurrentHp = stats?.CurrentHp ?? 0,
                    MaxHp = stats?.MaxHp ?? 0,
                    DamageTaken = stats?.DamageTaken ?? 0,
                    HpHealed = stats?.HpHealed ?? 0,
                    MaxHpGained = stats?.MaxHpGained ?? 0,
                    MaxHpLost = stats?.MaxHpLost ?? 0,
                    CurrentGold = stats?.CurrentGold ?? 0,
                    GoldGained = stats?.GoldGained ?? 0,
                    GoldSpent = stats?.GoldSpent ?? 0,
                    GoldLost = stats?.GoldLost ?? 0,
                    GoldStolen = stats?.GoldStolen ?? 0
                };

                floors.Add(floor);
                floorDataList.Add(MapFloorData(mapPoint, stats));
            }
        }

        return (run, floors, floorDataList);
    }

    private static FloorData MapFloorData(MapPoint mapPoint, PlayerFloorStats? stats)
    {
        var isShop = mapPoint.MapPointType == "shop";
        var room = mapPoint.Rooms.Count > 0 ? mapPoint.Rooms[0] : null;
        var boughtColorless = stats?.BoughtColorless ?? [];
        var boughtRelics = stats?.BoughtRelics ?? [];

        // Card choices
        var cardChoices = new List<CardChoiceEntity>();
        foreach (var cc in stats?.CardChoices ?? [])
        {
            cardChoices.Add(new CardChoiceEntity
            {
                CardId = cc.Card.Id,
                WasPicked = cc.WasPicked,
                WasBought = boughtColorless.Contains(cc.Card.Id),
                UpgradeLevel = cc.Card.CurrentUpgradeLevel ?? 0,
                EnchantmentId = cc.Card.Enchantment?.Id,
                EnchantmentAmount = cc.Card.Enchantment?.Amount ?? 0,
                Source = isShop ? "shop" : "reward"
            });
        }

        // Relic choices
        var relicChoices = new List<RelicChoiceEntity>();
        foreach (var rc in stats?.RelicChoices ?? [])
        {
            relicChoices.Add(new RelicChoiceEntity
            {
                RelicId = rc.Choice,
                WasPicked = rc.WasPicked,
                WasBought = boughtRelics.Contains(rc.Choice),
                Source = isShop ? "shop" : "reward"
            });
        }

        // Potion choices
        var potionChoices = new List<PotionChoiceEntity>();
        foreach (var pc in stats?.PotionChoices ?? [])
        {
            potionChoices.Add(new PotionChoiceEntity
            {
                PotionId = pc.Choice,
                WasPicked = pc.WasPicked
            });
        }

        // Potion events
        var potionEvents = new List<PotionEventEntity>();
        foreach (var pu in stats?.PotionUsed ?? [])
        {
            potionEvents.Add(new PotionEventEntity { PotionId = pu, Action = "used" });
        }
        foreach (var pd in stats?.PotionDiscarded ?? [])
        {
            potionEvents.Add(new PotionEventEntity { PotionId = pd, Action = "discarded" });
        }

        // Event choices
        var eventChoices = new List<EventChoiceEntity>();
        foreach (var ec in stats?.EventChoices ?? [])
        {
            eventChoices.Add(new EventChoiceEntity
            {
                EventId = room?.ModelId,
                ChoiceKey = ec.Title?.Key,
                ChoiceTable = ec.Title?.Table,
                Variables = ec.Variables != null
                    ? JsonSerializer.Serialize(ec.Variables)
                    : null
            });
        }

        // Rest site choices
        var restSiteChoices = new List<RestSiteChoiceEntity>();
        foreach (var rsc in stats?.RestSiteChoices ?? [])
        {
            restSiteChoices.Add(new RestSiteChoiceEntity { Choice = rsc });
        }

        var restSiteUpgradedCards = stats?.UpgradedCards?.ToList() ?? [];

        // Card transforms
        var cardTransforms = new List<CardTransformEntity>();
        foreach (var ct in stats?.CardsTransformed ?? [])
        {
            cardTransforms.Add(new CardTransformEntity
            {
                OriginalCardId = ct.OriginalCard.Id,
                FinalCardId = ct.FinalCard.Id
            });
        }

        // Monsters
        var monsters = new List<MonsterEntity>();
        foreach (var mid in room?.MonsterIds ?? [])
        {
            monsters.Add(new MonsterEntity { MonsterId = mid });
        }

        // Cards gained
        var cardsGained = new List<CardsGainedEntity>();
        foreach (var cg in stats?.CardsGained ?? [])
        {
            cardsGained.Add(new CardsGainedEntity
            {
                CardId = cg.Id,
                UpgradeLevel = cg.CurrentUpgradeLevel ?? 0,
                EnchantmentId = cg.Enchantment?.Id,
                Source = isShop ? "shop" : "reward"
            });
        }

        // Card removals
        var cardRemovals = new List<CardRemovalEntity>();
        foreach (var cr in stats?.CardsRemoved ?? [])
        {
            cardRemovals.Add(new CardRemovalEntity
            {
                CardId = cr.Id,
                FloorAddedToDeck = cr.FloorAddedToDeck
            });
        }

        // Card enchantments
        var cardEnchantments = new List<CardEnchantmentEntity>();
        foreach (var ce in stats?.CardsEnchanted ?? [])
        {
            cardEnchantments.Add(new CardEnchantmentEntity
            {
                CardId = ce.Card.Id,
                EnchantmentId = ce.Enchantment,
                EnchantmentAmount = ce.Card.Enchantment?.Amount ?? 0
            });
        }

        // Ancient choices
        var ancientChoices = new List<AncientChoiceEntity>();
        foreach (var ac in stats?.AncientChoice ?? [])
        {
            ancientChoices.Add(new AncientChoiceEntity
            {
                TextKey = ac.TextKey,
                WasChosen = ac.WasChosen
            });
        }

        return new FloorData(
            cardChoices, relicChoices, potionChoices, potionEvents,
            eventChoices, restSiteChoices, restSiteUpgradedCards,
            cardTransforms, monsters, cardsGained, cardRemovals,
            cardEnchantments, ancientChoices
        );
    }
}

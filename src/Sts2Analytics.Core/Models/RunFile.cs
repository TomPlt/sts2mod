using System.Text.Json.Serialization;

namespace Sts2Analytics.Core.Models;

public record RunFile
{
    [JsonPropertyName("acts")]
    public List<string> Acts { get; init; } = [];

    [JsonPropertyName("ascension")]
    public int Ascension { get; init; }

    [JsonPropertyName("build_id")]
    public string BuildId { get; init; } = "";

    [JsonPropertyName("game_mode")]
    public string GameMode { get; init; } = "";

    [JsonPropertyName("killed_by_encounter")]
    public string? KilledByEncounter { get; init; }

    [JsonPropertyName("killed_by_event")]
    public string? KilledByEvent { get; init; }

    [JsonPropertyName("map_point_history")]
    public List<List<MapPoint>> MapPointHistory { get; init; } = [];

    [JsonPropertyName("modifiers")]
    public List<string> Modifiers { get; init; } = [];

    [JsonPropertyName("platform_type")]
    public string PlatformType { get; init; } = "";

    [JsonPropertyName("players")]
    public List<PlayerData> Players { get; init; } = [];

    [JsonPropertyName("run_time")]
    public long RunTime { get; init; }

    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("seed")]
    public string Seed { get; init; } = "";

    [JsonPropertyName("start_time")]
    public long StartTime { get; init; }

    [JsonPropertyName("was_abandoned")]
    public bool WasAbandoned { get; init; }

    [JsonPropertyName("win")]
    public bool Win { get; init; }
}

public record MapPoint
{
    [JsonPropertyName("map_point_type")]
    public string MapPointType { get; init; } = "";

    [JsonPropertyName("player_stats")]
    public List<PlayerFloorStats> PlayerStats { get; init; } = [];

    [JsonPropertyName("rooms")]
    public List<Room> Rooms { get; init; } = [];
}

public record Room
{
    [JsonPropertyName("model_id")]
    public string? ModelId { get; init; }

    [JsonPropertyName("monster_ids")]
    public List<string>? MonsterIds { get; init; }

    [JsonPropertyName("room_type")]
    public string RoomType { get; init; } = "";

    [JsonPropertyName("turns_taken")]
    public int TurnsTaken { get; init; }
}

public record PlayerFloorStats
{
    [JsonPropertyName("player_id")]
    public int PlayerId { get; init; }

    [JsonPropertyName("current_hp")]
    public int CurrentHp { get; init; }

    [JsonPropertyName("max_hp")]
    public int MaxHp { get; init; }

    [JsonPropertyName("damage_taken")]
    public int DamageTaken { get; init; }

    [JsonPropertyName("hp_healed")]
    public int HpHealed { get; init; }

    [JsonPropertyName("max_hp_gained")]
    public int MaxHpGained { get; init; }

    [JsonPropertyName("max_hp_lost")]
    public int MaxHpLost { get; init; }

    [JsonPropertyName("current_gold")]
    public int CurrentGold { get; init; }

    [JsonPropertyName("gold_gained")]
    public int GoldGained { get; init; }

    [JsonPropertyName("gold_spent")]
    public int GoldSpent { get; init; }

    [JsonPropertyName("gold_lost")]
    public int GoldLost { get; init; }

    [JsonPropertyName("gold_stolen")]
    public int GoldStolen { get; init; }

    [JsonPropertyName("card_choices")]
    public List<CardChoiceEntry>? CardChoices { get; init; }

    [JsonPropertyName("cards_gained")]
    public List<CardEntry>? CardsGained { get; init; }

    [JsonPropertyName("cards_removed")]
    public List<CardEntry>? CardsRemoved { get; init; }

    [JsonPropertyName("cards_transformed")]
    public List<CardTransformEntry>? CardsTransformed { get; init; }

    [JsonPropertyName("cards_enchanted")]
    public List<CardEnchantedEntry>? CardsEnchanted { get; init; }

    [JsonPropertyName("relic_choices")]
    public List<RelicChoiceEntry>? RelicChoices { get; init; }

    [JsonPropertyName("bought_relics")]
    public List<string>? BoughtRelics { get; init; }

    [JsonPropertyName("bought_colorless")]
    public List<string>? BoughtColorless { get; init; }

    [JsonPropertyName("potion_choices")]
    public List<PotionChoiceEntry>? PotionChoices { get; init; }

    [JsonPropertyName("potion_used")]
    public List<string>? PotionUsed { get; init; }

    [JsonPropertyName("potion_discarded")]
    public List<string>? PotionDiscarded { get; init; }

    [JsonPropertyName("event_choices")]
    public List<EventChoiceEntry>? EventChoices { get; init; }

    [JsonPropertyName("ancient_choice")]
    public List<AncientChoiceEntry>? AncientChoice { get; init; }

    [JsonPropertyName("rest_site_choices")]
    public List<string>? RestSiteChoices { get; init; }

    [JsonPropertyName("upgraded_cards")]
    public List<string>? UpgradedCards { get; init; }
}

public record CardChoiceEntry
{
    [JsonPropertyName("card")]
    public CardEntry Card { get; init; } = new();

    [JsonPropertyName("was_picked")]
    public bool WasPicked { get; init; }
}

public record CardEntry
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("current_upgrade_level")]
    public int? CurrentUpgradeLevel { get; init; }

    [JsonPropertyName("floor_added_to_deck")]
    public int? FloorAddedToDeck { get; init; }

    [JsonPropertyName("enchantment")]
    public EnchantmentEntry? Enchantment { get; init; }
}

public record EnchantmentEntry
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("amount")]
    public int Amount { get; init; }
}

public record CardTransformEntry
{
    [JsonPropertyName("original_card")]
    public CardEntry OriginalCard { get; init; } = new();

    [JsonPropertyName("final_card")]
    public CardEntry FinalCard { get; init; } = new();
}

public record CardEnchantedEntry
{
    [JsonPropertyName("card")]
    public CardEntry Card { get; init; } = new();

    [JsonPropertyName("enchantment")]
    public string Enchantment { get; init; } = "";
}

public record RelicChoiceEntry
{
    [JsonPropertyName("choice")]
    public string Choice { get; init; } = "";

    [JsonPropertyName("was_picked")]
    public bool WasPicked { get; init; }
}

public record PotionChoiceEntry
{
    [JsonPropertyName("choice")]
    public string Choice { get; init; } = "";

    [JsonPropertyName("was_picked")]
    public bool WasPicked { get; init; }
}

public record EventChoiceEntry
{
    [JsonPropertyName("title")]
    public LocalizedText? Title { get; init; }

    [JsonPropertyName("variables")]
    public Dictionary<string, object>? Variables { get; init; }
}

public record LocalizedText
{
    [JsonPropertyName("key")]
    public string Key { get; init; } = "";

    [JsonPropertyName("table")]
    public string Table { get; init; } = "";
}

public record AncientChoiceEntry
{
    [JsonPropertyName("TextKey")]
    public string TextKey { get; init; } = "";

    [JsonPropertyName("title")]
    public LocalizedText? Title { get; init; }

    [JsonPropertyName("was_chosen")]
    public bool WasChosen { get; init; }
}

public record PlayerData
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("character")]
    public string Character { get; init; } = "";

    [JsonPropertyName("max_potion_slot_count")]
    public int MaxPotionSlotCount { get; init; }

    [JsonPropertyName("deck")]
    public List<CardEntry> Deck { get; init; } = [];

    [JsonPropertyName("relics")]
    public List<RelicEntry> Relics { get; init; } = [];

    [JsonPropertyName("potions")]
    public List<PotionEntry> Potions { get; init; } = [];
}

public record RelicEntry
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("floor_added_to_deck")]
    public int FloorAddedToDeck { get; init; }

    [JsonPropertyName("props")]
    public object? Props { get; init; }
}

public record PotionEntry
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("slot_index")]
    public int SlotIndex { get; init; }
}

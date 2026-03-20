namespace Sts2Analytics.Core.Models;

public record RunEntity
{
    public long Id { get; init; }
    public string FileName { get; init; } = "";
    public string Seed { get; init; } = "";
    public string Character { get; init; } = "";
    public int Ascension { get; init; }
    public string GameMode { get; init; } = "";
    public string BuildVersion { get; init; } = "";
    public bool Win { get; init; }
    public bool WasAbandoned { get; init; }
    public string KilledByEncounter { get; init; } = "";
    public string KilledByEvent { get; init; } = "";
    public string StartTime { get; init; } = "";
    public double RunTime { get; init; }
    public int Acts { get; init; }
    public int SchemaVersion { get; init; }
    public string PlatformType { get; init; } = "";
    public string? Modifiers { get; init; }
    public int MaxPotionSlots { get; init; }
}

public record FloorEntity
{
    public long Id { get; init; }
    public long RunId { get; init; }
    public int ActIndex { get; init; }
    public int FloorIndex { get; init; }
    public string MapPointType { get; init; } = "";
    public string? EncounterId { get; init; }
    public string? RoomType { get; init; }
    public int TurnsTaken { get; init; }
    public int PlayerId { get; init; }
    public int CurrentHp { get; init; }
    public int MaxHp { get; init; }
    public int DamageTaken { get; init; }
    public int HpHealed { get; init; }
    public int MaxHpGained { get; init; }
    public int MaxHpLost { get; init; }
    public int CurrentGold { get; init; }
    public int GoldGained { get; init; }
    public int GoldSpent { get; init; }
    public int GoldLost { get; init; }
    public int GoldStolen { get; init; }
}

public record CardChoiceEntity
{
    public long Id { get; init; }
    public long FloorId { get; init; }
    public string CardId { get; init; } = "";
    public bool WasPicked { get; init; }
    public bool WasBought { get; init; }
    public int UpgradeLevel { get; init; }
    public string? EnchantmentId { get; init; }
    public int EnchantmentAmount { get; init; }
    public string Source { get; init; } = "";
}

public record RelicChoiceEntity
{
    public long Id { get; init; }
    public long FloorId { get; init; }
    public string RelicId { get; init; } = "";
    public bool WasPicked { get; init; }
    public bool WasBought { get; init; }
    public string Source { get; init; } = "";
}

public record PotionChoiceEntity
{
    public long Id { get; init; }
    public long FloorId { get; init; }
    public string PotionId { get; init; } = "";
    public bool WasPicked { get; init; }
}

public record PotionEventEntity
{
    public long Id { get; init; }
    public long FloorId { get; init; }
    public string PotionId { get; init; } = "";
    public string Action { get; init; } = "";
}

public record EventChoiceEntity
{
    public long Id { get; init; }
    public long FloorId { get; init; }
    public string? EventId { get; init; }
    public string? ChoiceKey { get; init; }
    public string? ChoiceTable { get; init; }
    public string? Variables { get; init; }
}

public record RestSiteChoiceEntity
{
    public long Id { get; init; }
    public long FloorId { get; init; }
    public string Choice { get; init; } = "";
}

public record RestSiteUpgradeEntity
{
    public long Id { get; init; }
    public long RestSiteChoiceId { get; init; }
    public string CardId { get; init; } = "";
}

public record CardTransformEntity
{
    public long Id { get; init; }
    public long FloorId { get; init; }
    public string OriginalCardId { get; init; } = "";
    public string FinalCardId { get; init; } = "";
}

public record MonsterEntity
{
    public long Id { get; init; }
    public long FloorId { get; init; }
    public string MonsterId { get; init; } = "";
}

public record FinalDeckEntity
{
    public long Id { get; init; }
    public long RunId { get; init; }
    public string CardId { get; init; } = "";
    public int UpgradeLevel { get; init; }
    public int FloorAdded { get; init; }
    public string? EnchantmentId { get; init; }
}

public record FinalRelicEntity
{
    public long Id { get; init; }
    public long RunId { get; init; }
    public string RelicId { get; init; } = "";
    public int FloorAdded { get; init; }
    public string? Props { get; init; }
}

public record FinalPotionEntity
{
    public long Id { get; init; }
    public long RunId { get; init; }
    public string PotionId { get; init; } = "";
    public int SlotIndex { get; init; }
}

public record CardsGainedEntity
{
    public long Id { get; init; }
    public long FloorId { get; init; }
    public string CardId { get; init; } = "";
    public int UpgradeLevel { get; init; }
    public string? EnchantmentId { get; init; }
    public string Source { get; init; } = "";
}

public record CardRemovalEntity
{
    public long Id { get; init; }
    public long FloorId { get; init; }
    public string CardId { get; init; } = "";
    public int? FloorAddedToDeck { get; init; }
}

public record CardEnchantmentEntity
{
    public long Id { get; init; }
    public long FloorId { get; init; }
    public string CardId { get; init; } = "";
    public string EnchantmentId { get; init; } = "";
    public int EnchantmentAmount { get; init; }
}

public record AncientChoiceEntity
{
    public long Id { get; init; }
    public long FloorId { get; init; }
    public string TextKey { get; init; } = "";
    public bool WasChosen { get; init; }
}

public record EloRatingEntity
{
    public long Id { get; init; }
    public string CardId { get; init; } = "";
    public string Character { get; init; } = "";
    public string Context { get; init; } = "overall";
    public double Rating { get; init; } = 1500.0;
    public int GamesPlayed { get; init; }
}

public record EloHistoryEntity
{
    public long Id { get; init; }
    public long EloRatingId { get; init; }
    public long RunId { get; init; }
    public double RatingBefore { get; init; }
    public double RatingAfter { get; init; }
    public string Timestamp { get; init; } = "";
}

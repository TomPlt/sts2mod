using System.Data;
using System.Text.Json;
using Dapper;
using Sts2Analytics.Core.Models;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Core.Database;

public class RunRepository
{
    private readonly IDbConnection _connection;

    public RunRepository(IDbConnection connection)
    {
        _connection = connection;
    }

    public long ImportRun(RunEntity run, List<FloorEntity> floors, List<FloorData> floorData, PlayerData player)
    {
        var existing = _connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM Runs WHERE FileName = @FileName",
            new { run.FileName });
        if (existing > 0)
            return -1;

        using var transaction = _connection.BeginTransaction();

        try
        {
            _connection.Execute("""
                INSERT INTO Runs (FileName, Seed, Character, Ascension, GameMode, BuildVersion,
                    Win, WasAbandoned, KilledByEncounter, KilledByEvent, StartTime, RunTime,
                    Acts, SchemaVersion, PlatformType, Modifiers, MaxPotionSlots, Source)
                VALUES (@FileName, @Seed, @Character, @Ascension, @GameMode, @BuildVersion,
                    @Win, @WasAbandoned, @KilledByEncounter, @KilledByEvent, @StartTime, @RunTime,
                    @Acts, @SchemaVersion, @PlatformType, @Modifiers, @MaxPotionSlots, @Source)
                """, run, transaction);

            var runId = _connection.ExecuteScalar<long>("SELECT last_insert_rowid()", transaction: transaction);

            for (int i = 0; i < floors.Count; i++)
            {
                var floor = floors[i];
                var fd = floorData[i];

                _connection.Execute("""
                    INSERT INTO Floors (RunId, ActIndex, FloorIndex, MapPointType, EncounterId,
                        RoomType, TurnsTaken, PlayerId, CurrentHp, MaxHp, DamageTaken, HpHealed,
                        MaxHpGained, MaxHpLost, CurrentGold, GoldGained, GoldSpent, GoldLost, GoldStolen)
                    VALUES (@RunId, @ActIndex, @FloorIndex, @MapPointType, @EncounterId,
                        @RoomType, @TurnsTaken, @PlayerId, @CurrentHp, @MaxHp, @DamageTaken, @HpHealed,
                        @MaxHpGained, @MaxHpLost, @CurrentGold, @GoldGained, @GoldSpent, @GoldLost, @GoldStolen)
                    """, new
                {
                    RunId = runId,
                    floor.ActIndex,
                    floor.FloorIndex,
                    floor.MapPointType,
                    floor.EncounterId,
                    floor.RoomType,
                    floor.TurnsTaken,
                    floor.PlayerId,
                    floor.CurrentHp,
                    floor.MaxHp,
                    floor.DamageTaken,
                    floor.HpHealed,
                    floor.MaxHpGained,
                    floor.MaxHpLost,
                    floor.CurrentGold,
                    floor.GoldGained,
                    floor.GoldSpent,
                    floor.GoldLost,
                    floor.GoldStolen
                }, transaction);

                var floorId = _connection.ExecuteScalar<long>("SELECT last_insert_rowid()", transaction: transaction);

                // Card choices
                foreach (var cc in fd.CardChoices)
                {
                    _connection.Execute("""
                        INSERT INTO CardChoices (FloorId, CardId, WasPicked, WasBought, UpgradeLevel,
                            EnchantmentId, EnchantmentAmount, Source)
                        VALUES (@FloorId, @CardId, @WasPicked, @WasBought, @UpgradeLevel,
                            @EnchantmentId, @EnchantmentAmount, @Source)
                        """, new
                    {
                        FloorId = floorId,
                        cc.CardId,
                        cc.WasPicked,
                        cc.WasBought,
                        cc.UpgradeLevel,
                        cc.EnchantmentId,
                        cc.EnchantmentAmount,
                        cc.Source
                    }, transaction);
                }

                // Relic choices
                foreach (var rc in fd.RelicChoices)
                {
                    _connection.Execute("""
                        INSERT INTO RelicChoices (FloorId, RelicId, WasPicked, WasBought, Source)
                        VALUES (@FloorId, @RelicId, @WasPicked, @WasBought, @Source)
                        """, new { FloorId = floorId, rc.RelicId, rc.WasPicked, rc.WasBought, rc.Source }, transaction);
                }

                // Potion choices
                foreach (var pc in fd.PotionChoices)
                {
                    _connection.Execute("""
                        INSERT INTO PotionChoices (FloorId, PotionId, WasPicked)
                        VALUES (@FloorId, @PotionId, @WasPicked)
                        """, new { FloorId = floorId, pc.PotionId, pc.WasPicked }, transaction);
                }

                // Potion events
                foreach (var pe in fd.PotionEvents)
                {
                    _connection.Execute("""
                        INSERT INTO PotionEvents (FloorId, PotionId, Action)
                        VALUES (@FloorId, @PotionId, @Action)
                        """, new { FloorId = floorId, pe.PotionId, pe.Action }, transaction);
                }

                // Event choices
                foreach (var ec in fd.EventChoices)
                {
                    _connection.Execute("""
                        INSERT INTO EventChoices (FloorId, EventId, ChoiceKey, ChoiceTable, Variables)
                        VALUES (@FloorId, @EventId, @ChoiceKey, @ChoiceTable, @Variables)
                        """, new { FloorId = floorId, ec.EventId, ec.ChoiceKey, ec.ChoiceTable, ec.Variables }, transaction);
                }

                // Rest site choices + upgrades
                foreach (var rsc in fd.RestSiteChoices)
                {
                    _connection.Execute("""
                        INSERT INTO RestSiteChoices (FloorId, Choice)
                        VALUES (@FloorId, @Choice)
                        """, new { FloorId = floorId, rsc.Choice }, transaction);

                    var restSiteChoiceId = _connection.ExecuteScalar<long>("SELECT last_insert_rowid()", transaction: transaction);

                    foreach (var cardId in fd.RestSiteUpgradedCards)
                    {
                        _connection.Execute("""
                            INSERT INTO RestSiteUpgrades (RestSiteChoiceId, CardId)
                            VALUES (@RestSiteChoiceId, @CardId)
                            """, new { RestSiteChoiceId = restSiteChoiceId, CardId = cardId }, transaction);
                    }
                }

                // Card transforms
                foreach (var ct in fd.CardTransforms)
                {
                    _connection.Execute("""
                        INSERT INTO CardTransforms (FloorId, OriginalCardId, FinalCardId)
                        VALUES (@FloorId, @OriginalCardId, @FinalCardId)
                        """, new { FloorId = floorId, ct.OriginalCardId, ct.FinalCardId }, transaction);
                }

                // Monsters
                foreach (var m in fd.Monsters)
                {
                    _connection.Execute("""
                        INSERT INTO Monsters (FloorId, MonsterId)
                        VALUES (@FloorId, @MonsterId)
                        """, new { FloorId = floorId, m.MonsterId }, transaction);
                }

                // Cards gained
                foreach (var cg in fd.CardsGained)
                {
                    _connection.Execute("""
                        INSERT INTO CardsGained (FloorId, CardId, UpgradeLevel, EnchantmentId, Source)
                        VALUES (@FloorId, @CardId, @UpgradeLevel, @EnchantmentId, @Source)
                        """, new { FloorId = floorId, cg.CardId, cg.UpgradeLevel, cg.EnchantmentId, cg.Source }, transaction);
                }

                // Card removals
                foreach (var cr in fd.CardRemovals)
                {
                    _connection.Execute("""
                        INSERT INTO CardRemovals (FloorId, CardId, FloorAddedToDeck)
                        VALUES (@FloorId, @CardId, @FloorAddedToDeck)
                        """, new { FloorId = floorId, cr.CardId, cr.FloorAddedToDeck }, transaction);
                }

                // Card enchantments
                foreach (var ce in fd.CardEnchantments)
                {
                    _connection.Execute("""
                        INSERT INTO CardEnchantments (FloorId, CardId, EnchantmentId, EnchantmentAmount)
                        VALUES (@FloorId, @CardId, @EnchantmentId, @EnchantmentAmount)
                        """, new { FloorId = floorId, ce.CardId, ce.EnchantmentId, ce.EnchantmentAmount }, transaction);
                }

                // Ancient choices
                foreach (var ac in fd.AncientChoices)
                {
                    _connection.Execute("""
                        INSERT INTO AncientChoices (FloorId, TextKey, WasChosen)
                        VALUES (@FloorId, @TextKey, @WasChosen)
                        """, new { FloorId = floorId, ac.TextKey, ac.WasChosen }, transaction);
                }
            }

            // Final deck
            foreach (var card in player.Deck)
            {
                _connection.Execute("""
                    INSERT INTO FinalDecks (RunId, CardId, UpgradeLevel, FloorAdded, EnchantmentId)
                    VALUES (@RunId, @CardId, @UpgradeLevel, @FloorAdded, @EnchantmentId)
                    """, new
                {
                    RunId = runId,
                    CardId = card.Id,
                    UpgradeLevel = card.CurrentUpgradeLevel ?? 0,
                    FloorAdded = card.FloorAddedToDeck ?? 0,
                    EnchantmentId = card.Enchantment?.Id
                }, transaction);
            }

            // Final relics
            foreach (var relic in player.Relics)
            {
                _connection.Execute("""
                    INSERT INTO FinalRelics (RunId, RelicId, FloorAdded, Props)
                    VALUES (@RunId, @RelicId, @FloorAdded, @Props)
                    """, new
                {
                    RunId = runId,
                    RelicId = relic.Id,
                    FloorAdded = relic.FloorAddedToDeck,
                    Props = relic.Props != null ? JsonSerializer.Serialize(relic.Props) : null
                }, transaction);
            }

            // Final potions
            foreach (var potion in player.Potions)
            {
                _connection.Execute("""
                    INSERT INTO FinalPotions (RunId, PotionId, SlotIndex)
                    VALUES (@RunId, @PotionId, @SlotIndex)
                    """, new
                {
                    RunId = runId,
                    PotionId = potion.Id,
                    SlotIndex = potion.SlotIndex
                }, transaction);
            }

            transaction.Commit();
            return runId;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }
}

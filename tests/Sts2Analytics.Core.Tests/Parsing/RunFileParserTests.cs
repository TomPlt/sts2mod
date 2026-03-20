using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Core.Tests.Parsing;

public class RunFileParserTests
{
    private readonly string _fixturePath = Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "sample_win.run");

    [Fact]
    public void Parse_SampleWinRun_ReturnsRunFileWithMetadata()
    {
        var result = RunFileParser.Parse(_fixturePath);

        Assert.Equal("J2R2Z14RCT", result.Seed);
        Assert.Equal(0, result.Ascension);
        Assert.Equal("standard", result.GameMode);
        Assert.True(result.Win);
        Assert.False(result.WasAbandoned);
        Assert.Equal("v0.98.0", result.BuildId);
        Assert.Equal(8, result.SchemaVersion);
        Assert.Equal("steam", result.PlatformType);
        Assert.Equal(3, result.Acts.Count);
    }

    [Fact]
    public void Parse_SampleWinRun_ReturnsPlayerData()
    {
        var result = RunFileParser.Parse(_fixturePath);

        Assert.Single(result.Players);
        Assert.Equal("CHARACTER.IRONCLAD", result.Players[0].Character);
        Assert.Equal(3, result.Players[0].MaxPotionSlotCount);
        Assert.True(result.Players[0].Deck.Count > 10);
        Assert.True(result.Players[0].Relics.Count > 5);
    }

    [Fact]
    public void Parse_SampleWinRun_ReturnsMapPointHistory()
    {
        var result = RunFileParser.Parse(_fixturePath);

        Assert.Equal(3, result.MapPointHistory.Count); // 3 acts
        Assert.True(result.MapPointHistory[0].Count > 5); // act 1 floors
        Assert.Equal("monster", result.MapPointHistory[0][0].MapPointType);
    }

    [Fact]
    public void Parse_SampleWinRun_ParsesCardChoices()
    {
        var result = RunFileParser.Parse(_fixturePath);
        var floor1 = result.MapPointHistory[0][0];
        var stats = floor1.PlayerStats[0];

        Assert.NotNull(stats.CardChoices);
        Assert.Equal(3, stats.CardChoices.Count);
        Assert.Equal("CARD.SETUP_STRIKE", stats.CardChoices[0].Card.Id);
        Assert.True(stats.CardChoices[0].WasPicked);
        Assert.Equal("CARD.TREMBLE", stats.CardChoices[1].Card.Id);
        Assert.False(stats.CardChoices[1].WasPicked);
    }

    [Fact]
    public void Parse_SampleWinRun_ParsesEnchantments()
    {
        var result = RunFileParser.Parse(_fixturePath);
        // Act 3 floor 1 (index 0) has enchanted card choices with ENCHANTMENT.GLAM
        var act3 = result.MapPointHistory[2];
        var monsterFloor = act3[1]; // second floor in act 3
        var stats = monsterFloor.PlayerStats[0];
        Assert.NotNull(stats.CardChoices);
        var pickedCard = stats.CardChoices.First(c => c.WasPicked);

        Assert.NotNull(pickedCard.Card.Enchantment);
        Assert.Equal("ENCHANTMENT.GLAM", pickedCard.Card.Enchantment.Id);
    }
}

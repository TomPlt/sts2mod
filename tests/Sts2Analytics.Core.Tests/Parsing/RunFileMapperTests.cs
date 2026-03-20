using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Core.Tests.Parsing;

public class RunFileMapperTests
{
    private readonly string _fixturePath = Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "sample_win.run");

    [Fact]
    public void MapRun_ExtractsRunMetadata()
    {
        var runFile = RunFileParser.Parse(_fixturePath);
        var (run, floors, _) = RunFileMapper.Map(runFile, "sample_win.run");
        Assert.Equal("sample_win.run", run.FileName);
        Assert.Equal("CHARACTER.IRONCLAD", run.Character);
        Assert.True(run.Win);
        Assert.Equal("J2R2Z14RCT", run.Seed);
    }

    [Fact]
    public void MapRun_ExtractsAllFloors()
    {
        var runFile = RunFileParser.Parse(_fixturePath);
        var (_, floors, _) = RunFileMapper.Map(runFile, "sample_win.run");
        Assert.True(floors.Count > 30);
        Assert.Equal("monster", floors[0].MapPointType);
        Assert.Equal(0, floors[0].ActIndex);
    }

    [Fact]
    public void MapRun_ExtractsCardChoicesWithSkips()
    {
        var runFile = RunFileParser.Parse(_fixturePath);
        var (_, _, floorData) = RunFileMapper.Map(runFile, "sample_win.run");
        var skippedFloor = floorData.First(f =>
            f.CardChoices.Count > 0 && f.CardChoices.All(c => !c.WasPicked));
        Assert.Equal(3, skippedFloor.CardChoices.Count);
    }
}

using Microsoft.Data.Sqlite;
using Dapper;
using Sts2Analytics.Core.Database;
using Sts2Analytics.Core.Parsing;

namespace Sts2Analytics.Core.Tests.Database;

public class RunRepositoryTests
{
    private readonly string _fixturePath = Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "sample_win.run");

    private SqliteConnection CreateDb()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Schema.Initialize(conn);
        return conn;
    }

    [Fact]
    public void ImportRun_InsertsAndReturnsId()
    {
        using var conn = CreateDb();
        var repo = new RunRepository(conn);
        var runFile = RunFileParser.Parse(_fixturePath);
        var (run, floors, floorData) = RunFileMapper.Map(runFile, "sample_win.run");
        var runId = repo.ImportRun(run, floors, floorData, runFile.Players[0]);
        Assert.True(runId > 0);
        Assert.Equal(1, conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Runs"));
    }

    [Fact]
    public void ImportRun_SkipsDuplicateFileName()
    {
        using var conn = CreateDb();
        var repo = new RunRepository(conn);
        var runFile = RunFileParser.Parse(_fixturePath);
        var (run, floors, floorData) = RunFileMapper.Map(runFile, "sample_win.run");
        repo.ImportRun(run, floors, floorData, runFile.Players[0]);
        var secondId = repo.ImportRun(run, floors, floorData, runFile.Players[0]);
        Assert.Equal(-1, secondId);
        Assert.Equal(1, conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Runs"));
    }

    [Fact]
    public void ImportRun_InsertsFloorAndChildData()
    {
        using var conn = CreateDb();
        var repo = new RunRepository(conn);
        var runFile = RunFileParser.Parse(_fixturePath);
        var (run, floors, floorData) = RunFileMapper.Map(runFile, "sample_win.run");
        repo.ImportRun(run, floors, floorData, runFile.Players[0]);

        Assert.True(conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Floors") > 30);
        Assert.True(conn.ExecuteScalar<int>("SELECT COUNT(*) FROM CardChoices") > 50);
        Assert.True(conn.ExecuteScalar<int>("SELECT COUNT(*) FROM FinalDecks") > 20);
    }
}

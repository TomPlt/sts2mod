using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sts2Analytics.Core.Models;

namespace Sts2Analytics.Web.Services;

public class ExportData
{
    public RunSummary? Summary { get; set; }
    public List<CardWinRate> CardWinRates { get; set; } = [];
    public List<CardPickRate> CardPickRates { get; set; } = [];
    public List<RelicWinRate> RelicWinRates { get; set; } = [];
    public List<RelicPickRate> RelicPickRates { get; set; } = [];
    public List<Glicko2RatingResult> Glicko2Ratings { get; set; } = [];
    /// <summary>Backward compat: old exports use "eloRatings" key. Merges into Glicko2Ratings.</summary>
    [JsonPropertyName("eloRatings")]
    public List<Glicko2RatingResult> EloRatings
    {
        get => [];
        set
        {
            if (value.Count > 0 && Glicko2Ratings.Count == 0)
                Glicko2Ratings = value;
        }
    }
    public List<CardChoiceExport> CardChoices { get; set; } = [];
    public List<RelicChoiceExport> RelicChoices { get; set; } = [];
    public List<RunExport> Runs { get; set; } = [];
    public List<DamageByEncounter> DamageByEncounter { get; set; } = [];
    public List<EliteCorrelation> EliteCorrelation { get; set; } = [];
    public List<EliteCorrelationByAct> EliteCorrelationByAct { get; set; } = [];
    public List<PlayerRatingExport> PlayerRatings { get; set; } = [];
    public List<PlayerRatingHistoryExport> PlayerRatingHistory { get; set; } = [];
    public List<BlindSpotExport> BlindSpots { get; set; } = [];
    public List<AncientRatingExport> AncientRatings { get; set; } = [];
    public List<RestSiteDecisionExport> RestSiteDecisions { get; set; } = [];
    public List<RestSiteHpBucketExport> RestSiteHpBuckets { get; set; } = [];
    public List<RestSiteUpgradeExport> RestSiteUpgrades { get; set; } = [];
    public List<RestSiteActExport> RestSiteActBreakdown { get; set; } = [];
}

public class CardChoiceExport
{
    public string CardId { get; set; } = "";
    public bool WasPicked { get; set; }
    public long RunId { get; set; }
}

public class RelicChoiceExport
{
    public string RelicId { get; set; } = "";
    public bool WasPicked { get; set; }
    public long RunId { get; set; }
}

public class RunExport
{
    public long Id { get; set; }
    public string Character { get; set; } = "";
    public int Ascension { get; set; }
    public bool Win { get; set; }
    public string Seed { get; set; } = "";
    public string GameMode { get; set; } = "";
}

public class PlayerRatingExport
{
    public string Context { get; set; } = "";
    public double Rating { get; set; }
    public double RatingDeviation { get; set; }
    public int GamesPlayed { get; set; }
}

public class PlayerRatingHistoryExport
{
    public string Context { get; set; } = "";
    public long RunId { get; set; }
    public double RatingBefore { get; set; }
    public double RatingAfter { get; set; }
    public string Opponent { get; set; } = "";
    public double Outcome { get; set; }
}

public class BlindSpotExport
{
    public string CardId { get; set; } = "";
    public string Context { get; set; } = "";
    public string BlindSpotType { get; set; } = "";
    public double Score { get; set; }
    public double PickRate { get; set; }
    public double ExpectedPickRate { get; set; }
    public double WinRateDelta { get; set; }
    public int GamesAnalyzed { get; set; }
}

public class RestSiteDecisionExport
{
    public string Choice { get; set; } = "";
    public int Count { get; set; }
    public int Wins { get; set; }
    public double WinRate { get; set; }
}

public class RestSiteHpBucketExport
{
    public string Choice { get; set; } = "";
    public int HpBucketMin { get; set; }
    public int HpBucketMax { get; set; }
    public int Count { get; set; }
    public int Wins { get; set; }
    public double WinRate { get; set; }
}

public class RestSiteUpgradeExport
{
    public string CardId { get; set; } = "";
    public int TimesUpgraded { get; set; }
    public int Wins { get; set; }
    public double WinRate { get; set; }
}

public class RestSiteActExport
{
    public int Act { get; set; }
    public string Choice { get; set; } = "";
    public int Count { get; set; }
    public int Wins { get; set; }
    public double WinRate { get; set; }
    public double AvgHpPercent { get; set; }
}

public class AncientRatingExport
{
    public string ChoiceKey { get; set; } = "";
    public string Character { get; set; } = "";
    public string Context { get; set; } = "";
    public double Rating { get; set; }
    public double RatingDeviation { get; set; }
    public int GamesPlayed { get; set; }
}

public class DataService
{
    private readonly HttpClient _http;
    private ExportData? _data;
    private Dictionary<string, string>? _cardRarities;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public DataService(HttpClient http) => _http = http;

    public async Task<ExportData> GetDataAsync()
    {
        if (_data is null)
        {
            var response = await _http.GetAsync("data.json");
            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync();
            _data = await JsonSerializer.DeserializeAsync<ExportData>(stream, JsonOptions) ?? new ExportData();
        }
        return _data;
    }

    public async Task<Dictionary<string, string>> GetCardRaritiesAsync()
    {
        if (_cardRarities is null)
        {
            var response = await _http.GetAsync("card_rarities.json");
            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync();
            _cardRarities = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, JsonOptions) ?? new();
        }
        return _cardRarities;
    }

    public string GetRarity(string cardId)
    {
        if (_cardRarities is null) return "Unknown";
        // Strip upgrade suffix for lookup
        var baseId = cardId.Contains('+') ? cardId[..cardId.IndexOf('+')] : cardId;
        if (_cardRarities.TryGetValue(cardId, out var r)) return r;
        if (_cardRarities.TryGetValue(baseId, out r)) return r;
        return "Unknown";
    }
}

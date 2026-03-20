using System.Net.Http.Json;
using System.Text.Json;
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
    public List<RunExport> Runs { get; set; } = [];
    public List<DamageByEncounter> DamageByEncounter { get; set; } = [];
    public List<EliteCorrelation> EliteCorrelation { get; set; } = [];
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

public class DataService
{
    private readonly HttpClient _http;
    private ExportData? _data;

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
}

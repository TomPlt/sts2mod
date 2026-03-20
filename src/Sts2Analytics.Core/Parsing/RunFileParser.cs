using System.Text.Json;
using Sts2Analytics.Core.Models;

namespace Sts2Analytics.Core.Parsing;

public static class RunFileParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static RunFile Parse(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<RunFile>(json, Options)
            ?? throw new InvalidOperationException($"Failed to parse run file: {filePath}");
    }

    public static RunFile ParseJson(string json)
    {
        return JsonSerializer.Deserialize<RunFile>(json, Options)
            ?? throw new InvalidOperationException("Failed to parse run JSON");
    }
}

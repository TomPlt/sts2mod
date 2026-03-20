namespace Sts2Analytics.Core.Models;

public record AnalyticsFilter(
    string? Character = null,
    int? AscensionMin = null,
    int? AscensionMax = null,
    DateTime? DateFrom = null,
    DateTime? DateTo = null,
    string? GameMode = null,
    int? ActIndex = null
);

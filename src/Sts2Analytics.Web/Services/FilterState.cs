using Sts2Analytics.Web.Services;

namespace Sts2Analytics.Web.Services;

public class FilterState
{
    public int? AscensionMin { get; set; }
    public int? AscensionMax { get; set; }
    public string? Character { get; set; }

    public event Action? OnChange;

    public void NotifyChanged() => OnChange?.Invoke();

    public bool MatchesRun(RunExport run)
    {
        if (Character != null && run.Character != Character) return false;
        if (AscensionMin.HasValue && run.Ascension < AscensionMin.Value) return false;
        if (AscensionMax.HasValue && run.Ascension > AscensionMax.Value) return false;
        return true;
    }

    public bool HasAscensionFilter => AscensionMin.HasValue || AscensionMax.HasValue;
    public bool HasAnyFilter => HasAscensionFilter || Character != null;
}

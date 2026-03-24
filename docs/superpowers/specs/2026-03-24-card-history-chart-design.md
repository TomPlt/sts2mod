# Card Rating History Chart

## Overview

New dashboard page (`/card-history`) showing how card ratings evolve over time. Users select 1-5 cards and toggle between Pick Elo, Outcome Elo, and Combat Elo metrics, displayed as line charts on a date timeline.

## Data Export

Add `cardRatingHistory` to the dashboard JSON export, queried from three history tables:

- `Glicko2History` → Pick Elo history
- `OutcomeGlicko2History` → Outcome Elo history
- `CombatGlicko2History` → Combat Elo history

Export structure per entry:
```json
{
  "cardId": "IRONCLAD.BASH",
  "metric": "pick",       // "pick" | "outcome" | "combat"
  "context": "overall",
  "rating": 1542.3,
  "rd": 120.5,
  "timestamp": "2026-03-15T10:30:00",
  "runId": 42
}
```

Scope constraints:
- Only "overall" context (ALL character, overall context) to limit JSON size
- One data point per card per run (the "after" rating)
- Cap at most recent ~200 runs worth of history
- Per-player history follows the existing `ByPlayer` pattern

## Dashboard Page

### Controls
- **Card multi-select**: searchable dropdown, 1-5 cards
- **Metric checkboxes**: Pick Elo, Outcome Elo, Combat Elo (at least one required)
- **Player/Character filters**: follow global `FilterState`

### Chart
- `RadzenChart` with `RadzenLineSeries` per card+metric combination
- X-axis: date timeline (`RadzenDateTimeAxis`)
- Y-axis: rating value
- Color per card, line style per metric (solid=Pick, dashed=Outcome, dotted=Combat)
- Legend showing card name + metric

### Navigation
- Add to sidebar nav as "Card History" under the existing "Card Ratings" link

## Files Changed

1. `ExportCommand.cs` — add card history export queries
2. `DataService.cs` — add `CardRatingHistory` property and model
3. New: `Pages/CardHistory.razor` — the chart page
4. `NavMenu.razor` or equivalent — add nav link

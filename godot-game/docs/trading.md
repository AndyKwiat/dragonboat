# Trading: cities, markets, crew, upkeep, time

Files: `src/trading/*` (rules, no Godot types), `src/core/GameData.cs`
(cities/products), `src/core/GameTime.cs` (the clock), `src/ui/CityPopup.cs`
(the market panel), `src/ui/Clock.cs` (purse/cargo/time readout), the
`// Market`, `// Player`, `// Game time`, `// Crew`, `// Upkeep`, `// City
popup` blocks of `src/core/Config.cs`. Everything here is a 1:1 port of
`../game/src` (`market.ts`, `player.ts`, `crew.ts`, `upkeep.ts`,
`gameTime.ts`) — when the rules change there, change them here the same way,
and keep these files free of Godot types so they stay a straight port.
Combat never touches any of this; the only shared objects are the `Boat`
(its `MaxSpeedScale` comes from the crew) and the clock.

## Data

`data/cities.json` and `data/products.json` are copies of `../game`'s,
generated there (`npm run cities` etc.; see `../game/AGENTS.md` for how
cities are placed and products dealt). Regenerate there and copy over.
`GameData.Load()` reads them once; `City` has a world position (y-down),
`Size` (small/medium/large), the product ids it trades and per-product
price overrides. Prices are never stored — `Market` derives them.

## Rules

- **Market** (`Market.cs`): sell price = ceil(basePrice × size multiplier ×
  local override), buy price = ceil(sell × `MarketBuyMarkup`). Always rounded
  up — the house wins. `MarketFor(city)` lists a `Quote` per product.
- **Player** (`Player.cs`): purse (may go negative), crew count, inventory.
  `Buy`/`Sell`/`Consume` clamp to what's held and what the hold can carry;
  `ChangeCrew` goes through `Crew.ChangeAllowed`.
- **Crew** (`Crew.cs`): head count drives top speed (`SpeedAtMin` →
  `SpeedAtMax` linearly up to `SpeedFullCrew` hands), hold space
  (`CargoPerCrew`), and you can't put hands ashore if the hold would then
  overflow. Hiring happens in large cities only (popup).
- **Upkeep** (`Upkeep.cs`): `Due` counts periods of game time elapsed since
  the last check (catching up over big jumps). `Rations`: one unit of
  `RationsProductId` (rice) per crew member per `RationsHoursPerUnit`;
  `Wages`: `WagesMoneyPerDay` per head per day. Both run in `Main` every
  frame and tell the open popup to refresh when something was charged.
- **Game time** (`GameTime.cs`): 30-day months, 24 h clock, only moves when
  the player acts: sailing advances `HoursPerSecondSailing` per (fast-forwarded)
  second under way, a transaction costs `MinutesPerTransaction`. Zoomed out
  the voyage runs in fast-forward (`TimeScaleForZoom`: `ZoomTimeNearRate` at
  `ZoomTimeNearZoom` up to `ZoomTimeFarRate` at `ZoomTimeFarZoom`) — the boat
  covers more ground and the clock moves by the same factor. Night is
  `NightStartHour`..`NightEndHour` (the clock tints).

## UI

Click a city's name (`ui/CityLabels.cs` does the hit-testing) to open
`CityPopup`: facts, the hiring hall (large cities), then one row per product
with buy/sell ×1/×10 buttons. Esc or the ✕ closes it. See [ui.md](ui.md) for
sizing/fonts. A click anywhere else closes it — and, since the combat port,
also fires the cannons at that spot; that's intended.

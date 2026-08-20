# Trading: cities, markets, story events, crew, upkeep, time

Files: `src/trading/*` (rules, no Godot types), `src/core/GameData.cs`
(cities/products), `src/core/GameTime.cs` (the clock), `src/ui/CityPopup.cs`
(the market panel), `src/ui/Clock.cs` (purse/cargo/time readout), the
`// Market`, `// Story events`, `// Player`, `// Game time`, `// Crew`,
`// Upkeep`, `// City popup` blocks of `src/core/Config.cs`. This started as
a 1:1 port of `../game/src` but the economy has since diverged (flat base
prices, structural discounts, story events) — this repo is now the source of
truth for these rules. Keep the `src/trading/` files free of Godot types.
Combat never touches any of this; the only shared objects are the `Boat`
(its `MaxSpeedScale` comes from the crew) and the clock.

## Data

`data/cities.json` and `data/products.json` are copies of `../game`'s,
generated there (`npm run cities` etc.; see `../game/AGENTS.md` for how
cities are placed and products dealt). Regenerate there and copy over.
`GameData.Load()` reads them once; `City` has a world position (y-down),
`Size` (small/medium/large) and the product ids it trades: large cities all
nine products, medium six staples + one luxury, small rice + one other
staple. (Any `priceOverrides` still in the JSON are ignored — that system
was replaced by story events.) Prices are never stored — `Market` derives
them.

## Rules

- **Market** (`Market.cs`): sell price = ceil(basePrice × multiplier), buy
  price = ceil(sell × `MarketBuyMarkup`). Always rounded up — the house
  wins. The multiplier comes from a live price story event if there is one;
  otherwise it's structural and permanent: a small city's one non-rice
  staple sells at `MarketSmallStapleDiscount` (60%) of base, a medium
  city's one luxury (`LuxuryProductIds`) at `MarketMediumLuxuryDiscount`
  (75%), everything else at base price. `MarketFor(city, storyEvents)`
  lists a `Quote` per product; the quote carries the event, if any, so the
  UI can show its expiry.
- **Story events** (`StoryEvents.cs`): the world always holds
  `StoryEventCount` (40) of them, each tied to a city — a city can hold
  several. The only kind so far is `ProductPriceStoryEvent`: one product
  the city trades — picked with weight 1/(cities trading it), so rare
  goods headline more events but ubiquitous rice keeps a small chance —
  at `StoryEventLowMultiplier` (50%) or `StoryEventHighMultiplier` (150%)
  of its base price, high `StoryEventHighOddsToOne` (2) times as likely
  as low, never two on the same (city, product). Each expires at a midnight
  `StoryEventMinDays`–`StoryEventMaxDays` (7–21) days out; expiries only
  happen at midnight, so `StoryEvents.Update()` (run every frame from
  `Main`) is a single compare until the clock crosses one, then a single
  pass drops lapsed events and rolls replacements at random cities. When
  the pass runs, `Main` rebuilds the open city popup, whose prices and
  days-left tags are baked into its rows, and the map labels' price intel.
- **Discovery**: opening a city's popup marks the city discovered forever
  (`City.Discovered`) and marks every event live there discovered
  (`StoryEvent.Discovered`; via `StoryEvents.DiscoverAt`, also called when
  the popup rebuilds at midnight while open). On the map, under a city's
  name label (`CityLabels.RefreshIntel`): a discovered event shows its
  product, price and days left even if the city itself is undiscovered; a
  discovered city additionally shows every product whose permanent
  structural price is off base. An undiscovered event on a discovered city
  shows nothing for that product — the player hasn't seen that market
  since the event began.
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
with buy/sell ×1/×10 buttons. A row whose sell price differs from the
product's base price is tinted (red = dear, green = cheap) with a ▲/▼
percentage of base; if the swing is a story event the row also shows how
many days it has left, e.g. `(12d)`. Esc, the ✕, or a click outside closes it. See
[ui.md](ui.md) for sizing/fonts. While the popup is open the world is paused
for input: no sailing, no camera pan/zoom, no cannon fire — and the click
that closes it is inert (see `menuOpen` in `Main._Process`).

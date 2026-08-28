# The economy: production, supply, prices

Files, bottom up — all Godot-free, all unit-tested (`dotnet test tests`):

| file | what it owns |
|---|---|
| `src/trading/Production.cs` | who makes what, and what distance from the producers does to a city's settled price |
| `src/trading/Supply.cs` | the price curve, the caps, the per-unit ladder, the daily decay, the world average |
| `src/trading/MarketDeal.cs` | one staged position in one market: anchoring, limits, exact refunds, cost basis, profit |
| `src/trading/WorldPrices.cs` | the daily fixing of what each product fetches worldwide |
| `src/trading/Market.cs` | quotes, `PriceReading` and the daily decay pass — what the UI draws from |

Plus `src/core/Product.cs` and the market fields on `src/core/City.cs`, the
`// Market` block of `src/core/Config.cs` (`Config.Market`, a `MarketTerms`),
and `src/trading/Player.cs` for the hold's book value (it implements
`ITrader`, which is all a deal needs of it). Tests are `tests/SupplyTests.cs`
(curve, caps, ladder, decay, world average, quotes),
`tests/ProductionTests.cs` (who makes what, distance pricing, the startup
check) and `tests/MarketDealTests.cs` (staging, limits, refunds, cost basis,
profit), sharing `tests/EconomyFixtures.cs`.

None of these files reach `GameData` or Godot — the product catalogue is
handed in — so anything you add here should stay that way and get a test.

Where this sits: [trading.md](trading.md) owns the cities themselves, story
events, docking and the popup around the market; this doc owns every number
in it. The rule of thumb — if it decides a **price**, it is here.

## Data

`data/products.json` is canonical. Every product carries a `class`:
**staple** (rice, fish, fruit, wood, wool) or **luxury** (clothes, saffron,
furniture, jewelry). The class decides who makes it and nothing else — a
luxury is not inherently dearer than a staple, its base price is.

What a city **produces** and what it **stocks** is dealt fresh every run
(`Production.Deal`, from `GameData.SetCities`):

- **Produces**: a small city makes 1 staple, a medium 1 staple and 1 luxury,
  a large 2 luxuries. Dealt round-robin from a shuffled bag per class and
  only refilled once empty, so every staple has within a couple of producers
  of every other staple, and likewise the luxuries. The two classes cannot
  match *each other* — the size mix in `game-config.yml` fixes how many slots
  each class gets (at 18/18/5 that is 36 staple slots over 5 staples and 28
  luxury slots over 4 luxuries).
- **Stocks**: medium and large cities stock all nine products; a small city
  stocks rice plus the staple it produces — so a small city that produces
  rice stocks rice and nothing else.
- Every product needs at least `Production.LocalProducerCount` (3) producing
  cities, because that is what prices distance. Fewer and `Main` prints a
  banner and quits rather than starting a run with no economy — raise the
  city counts in `game-config.yml`.

Prices are never stored; `Supply.cs` derives them from two numbers the city
does hold, `LocalBase` and `Supply`.

Because names and positions are random per run, `--city=Name` in a scripted
run needs a name from *that* run's startup output.

## The three numbers behind every price

A quote is never stored. It is derived, every time, from:

1. the product's **base price** (`products.json`) — the world-wide anchor,
2. the city's **`LocalBase`** for it — what this port settles at when nothing
   is out of the ordinary, fixed for the run by distance from the producers,
3. the market's live **`Supply`** — its stock relative to normal, the only
   one of the three that moves during play.

## Rules

- **Market** (`Supply.cs`, `Market.cs`): a price is a function of one number,
  the market's **supply** — its stock relative to normal, held per (city,
  product) and clamped to ±**market size** (`MarketSize`: 20 small, 200
  medium, 2000 large). Negative is a shortage, positive a glut, 0 settled.
  Given `r = supply / MarketSize`:

  | `r` | price |
  |---|---|
  | `-1` | `basePrice × MaxOfBase` (2.5) — the ceiling, the same everywhere |
  | `0` | the city's `LocalBase` |
  | `+1` | `LocalBase × GlutFactor` (0.8) |

  linear between. The city **pays** `ceil(price)` and **charges**
  `ceil(ceil(price) × BuyMarkup)` (1.1) — always rounded up, the house wins
  the rounding. A quote is the price of the *next single unit* and moves as
  soon as one changes hands.
- **`LocalBase`** (`Production.SetLocalPrices`): what a city settles at when
  supply is 0, fixed for the run. For each product a city stocks, take the
  distance to the 3 nearest cities producing it — the city itself counts, at
  distance 0 — average them, divide by the map width, **halve the result if
  the city is itself a producer** (`LocalProducerDistanceFactor`: the goods
  are not just near, they are on the quay, so a producer sits well down the
  cheap end rather than merely scoring one distance of 0 out of three), then
  **normalise across every (city, product) pair in the run** so the world's
  spread fills 0..1. That fraction `m` gives `LocalBase = basePrice × (1 + m ×
  LocalMaxMarkup)` (markup 1.0), so the market closest to its producers
  settles at base price and the most remote at double it. Note the shortage
  ceiling above is *not* scaled by distance: remoteness makes a place dear at
  rest, it does not raise what a panic can charge there. A producer city is
  still not guaranteed to sit at base price — it is the cheapest source for a
  long way around, and the halving is what makes that gap worth sailing for.
- **Moving supply**: buying pushes it down (dearer), selling pushes it up
  (cheaper), **one unit at a time** — each unit is priced one step further
  along the ladder, so you cannot buy a market out at its floor price or
  dump a hold at its ceiling. The player can never buy more than
  `supply + MarketSize` units nor sell more than `MarketSize - supply`,
  which is what keeps supply inside its bounds. A unit is priced at the
  *scarcer* of the two levels it moves the market between, the same point in
  both directions, which is what makes a bought unit sell back for no more
  than it cost.
- **Decay** (`Market.DecayAll`, `Main`'s `DayTurn`): every midnight each
  market's supply loses a tenth of its magnitude, truncated toward zero, so
  it actually reaches 0 rather than creeping at it. That is the rest of the
  coast's traders quietly restocking and drawing down; it also relaxes story
  shocks and undoes the player's own dent in a market over a couple of weeks.
- **The world average** (`Supply.WorldAverage`, held by `WorldPrices`): what
  a product fetches across the whole
  world right now — the mean of every stocking city's quoted sell price **weighted
  by that city's market size**, so it is the price trade actually happens at
  rather than the average of the signs in the windows. A large city moves a
  hundred times what a small one does and counts for a hundred times as much;
  with the default 18/18/5 mix the large cities carry about 72% of the
  weight, so the figure tracks the big ports. This is what every percentage
  in the UI is measured against — `▲ 163%` means dear compared with the rest
  of the world *today*, not compared with a constant in a data file.
  A `WorldPrices` is built already populated and refreshed at every midnight
  — walking every city's every product is far too much per frame, and a daily
  fixing is what a trader would have heard anyway. `Main.RefreshMarkets`
  re-fixes it, rebuilds the open market and refreshes the map intel together.
  It is an object Main hands to the popup and the labels, not a static cache,
  so there is no window in which it can quietly answer with base prices.
- **Giving**: a product the city is paying at least `MarketGiveMinRatio`
  (1.5) times the world average for grows a Give button: donate every unit held for
  no money and one honour point per ten units, rounded down but never less
  than 1 (`Player.GiveAway`).
- **The staged deal** (`MarketDeal.cs`): one product's position in one
  market — what the slider moves, and where all of the subtlety lives. A deal
  is **anchored**: it remembers the market's stock level, the units aboard
  and what they cost at the moment the row opened, and prices every position
  off that anchor rather than accumulating leg by leg. That is what makes
  moving to N units and back to 0 return the money to the coin and the hold's
  book value to exactly where it started — undoing a buy is not a sell, so
  the markup never bites while you are still deciding. It also owns the three
  limits (hold, purse walking the ladder, and what the market can part with
  or absorb), the profit on a sale, and the slider's ±1 mapping. The anchor
  is only good while the row is open, which is why anything that moves the
  market or the hold behind its back rebuilds the rows
  (`CityPopup.Rebuild`, `Main.RefreshMarkets`).
  It talks to the player through `ITrader`, so every case above is tested
  against a stand-in with no Godot behind it (`tests/MarketDealTests.cs`).
- **The hold's book value** (`Player`'s `basis`, reached via `ITrader`): what
  the units aboard cost, per product, kept as a running total beside the
  count. `AverageCost` is that over the count — the number to hold a city's
  sell price against. Units always leave at the average price they went on at
  (`Consume`, so a sale, a gift and the crew's dinner all treat it the same),
  which keeps the basis proportional to the count however the cargo was
  accumulated. The hold you start the game with is booked at base price — it
  was not bought anywhere.

## How it reads on screen

The market panel (`src/ui/CityPopup.cs`) and the map labels
(`src/ui/CityLabels.cs`) both go through `Market.Read`, so they can never
disagree about whether a price is dear.

**The trade slider**, one per stocked product: centre = no deal, left sells a
fraction of the stock aboard (full left = all of it), right buys a fraction
of the most that fits in the hold, that the purse can pay for walking the
price ladder, and that the market can actually part with. All of it — the
limits, the pricing, the refund on the way back to centre, the hold's book
value — is `MarketDeal`; the popup owns nothing but the knob. Deals apply to
the player *and to the city's supply* as it moves, and every row's prices are
re-read on each refresh, so buying out a small market visibly drives its own
price up under your hand, and sliding back to centre is always free.

A row whose sell price is off the world average is tinted (red = dear, green
= cheap) with a ▲/▼ percentage **of that average**; a live supply shock adds how many
days it has left, e.g. `(7d)`. A good the city **produces** carries a
`◆ PRODUCES` mark in amber. Each row shows what is aboard and what it cost a
unit (`have 20 / paid 5`), and a staged **sale** adds a third line with the
profit or loss on it — revenue minus what those units cost to get — tinted
green or red, so you can tell at the knob whether the call was worth making.
The purse line carries what the whole hold cost. Under each row a small grey
debug line shows the three numbers behind the price: `supply n/MarketSize`,
the city's `local` price, and the `world` average.

Under a city's map label (`CityLabels.RefreshIntel`): now that every market
has its own resting price, listing all nine would bury the label, so a
discovered city shows only the products furthest from the world average,
most extreme first, capped at `CityIntelMaxLines` (3) and only past
`CityIntelMinDeviation` (20%). A discovered supply shock always shows, with
its days left, even where the city itself was never visited.

The city facts line carries what the city **makes** and its market size.

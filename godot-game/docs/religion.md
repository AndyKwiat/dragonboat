# Religion: piety, monasteries, monk errands

Files: `src/religion/Monasteries.cs` (all the rules, no Godot types —
unit-tested in `tests/MonasteriesTests.cs`), `src/ui/MonasteryPopup.cs` (the
monastery window), the `// Religion` block of `src/core/Config.cs`, plus
`Player.Piety`, the monastery row in `src/ui/CityPopup.cs`, the piety and
monk lines in `src/ui/Clock.cs` and the map tags in `src/ui/CityLabels.cs`.
Wired in `Main.cs`.

Religion is its own system: it moves the player's money and piety and reads
the clock, and never reaches into trading, banking or combat.

## Piety

A third currency beside money and honour (`Player.Piety`, shown in the clock
overlay under them). It starts at 0 and may go negative. Alms and landed
monks earn it; a monk who runs out of time takes 1000 of it away.

## Where the monasteries are

One small city in `Religion.SmallCitiesPerMonastery` (5) has a house of
monks — never a medium or large one. Which ones is drawn from a shuffle of
the small cities at launch off the world RNG, so `--seed` fixes them; the
run prints them (`Monasteries in Tern Rock (1753,1557), …`).

**A monastery is visible from the deck**: its city's map label carries a
`✦ MONASTERY` tag *even when the city has never been visited*, unlike the
price intel, which needs discovery. You never have to dock to find out.

The city's market popup grows a **Monastery** heading with a one-line
statement (alms today or the days to wait, and whether a monk is asking for
passage) and a button; the button opens `MonasteryPopup`, a second panel
over the market panel — the same arrangement as the bank's. Esc or a click
off it closes the monastery window and leaves the market open underneath.

## Alms for the poor

- A gift is `Religion.GiveMin`–`GiveMax` (100–2000) money in whole
  `GiveStep` (100) steps, chosen with a slider and capped by the purse, and
  it earns money ÷ `GivePietyDivisor` (10) piety — 200 given is 20 piety.
- **Once every `GiveCooldownDays` (7) days per house.** Each monastery
  keeps its own week (`Monastery.LastGiftDay`); while it is inside that
  week the window says how many days are left to wait instead of offering
  the slider, and `Monasteries.Give` refuses.

## Monk errands

- A house either has an errand waiting or it does not, and **that state is
  its own** — it is rolled on the daily pass, never when the player walks
  in, so there is nothing to gain by leaving and coming back.
- Every day, one house in `MonkOfferOddsToOne` (10) changes its mind: a
  house with no errand finds one, and an errand nobody has taken lapses.
  Equal odds either way means the world settles at about **half the houses
  having an errand** at any moment (the constructor starts them there with a
  coin flip each, so it holds from the first day). A house offers nothing
  while its own monk is still aboard.
- The monk is bound for a **large city**, drawn at random when the errand
  appears (so the destination is stable while it waits). Accepting puts him
  aboard with `MonkTermDays` (30) days to get there. Monks take no cargo
  space and several may ride at once.
- **Landing him** — opening the destination city's popup — pays
  `MonkPiety` (1000) piety, with a note at the top of that popup. There is
  no bonus for speed: unlike a VIP passage ([trading.md](trading.md)), a
  monk is worth the same on day 1 and day 30.
- **Running out of time**: on the day the term lapses he gives up and
  leaves, costing `MonkPenalty` (1000) piety (which may take the player
  negative), announced in the event log. The last `MonkWarnDays` (3) days
  each post a warning.
- `Monasteries.Update(today, losePiety)` runs from `Main` every frame and is
  a single compare until midnight turns over — the same daily beat as the
  bank loans and the VIP passengers (`DayTurn` / `Days.Of`, `core/News.cs`).

## Where the player sees it

- **Clock overlay**: `piety N` under honour (red when negative), and one
  `MONK delivery - Redcliff (due in 12d)` line per monk aboard, under the
  VIP lines.
- **Map labels**: `✦ MONASTERY` on every city that has one, discovered or
  not; `✦ MONK TO LAND HERE (12d)` on a city a monk aboard is bound for;
  and `▲ VIP TO LAND HERE` for VIP passengers, so sailing past either
  destination is a reminder to put in ([ui.md](ui.md)).
- **Event log**: the warnings, the walk-away, and a line when alms are given
  or an errand accepted.

## Test hooks

| flag | effect |
|---|---|
| `--monastery[=Name]` | open a monastery's window over its city's market (the first house, or a named city's) |
| `--monk[=daysAboard]` | put a monk aboard from that house, accepted that many days ago (the daily pass then runs on the first frame, so a warning or the walk-away lands at once) |
| `--alms=daysAgo` | pretend that house last took alms that many days ago, to see the cooldown |
| `--deliver` | with `--monk`, open the destination's market so he lands |

```bash
$G --path . -- --screenshot=x.png --seed=7 --monastery              # the monastery window
$G --path . -- --screenshot=x.png --seed=7 --monastery --alms=2     # …inside its week
$G --path . -- --screenshot=x.png --seed=7 --monk=5 --deliver       # land a monk: +1000 piety
$G --path . -- --screenshot=x.png --seed=7 --monk=30                # he gives up: -1000 piety
```

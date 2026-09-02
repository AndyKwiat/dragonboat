# The crew's morale

Files: `src/crew/Morale.cs` (all of the rules, Godot-free and unit-tested in
`tests/MoraleTests.cs`), `src/ui/MoraleBar.cs` (the bar under the oars bar),
the pub-night row in `src/ui/CityPopup.cs`, and the wiring — decay, the
speed it costs, hits, kills, calling at a city and `Main.PubNight` — in
`src/Main.cs`.

Morale is one number: how willing the crew are, `0` (mutinous) to `1`
(cheerful). It starts at **50%** and it only ever goes down on its own, so
a long passage with nowhere to put in wears the crew out whatever else
happens.

## What moves it

| | |
|---|---|
| time passing | **−1% every 4 game hours** (`FallPerHour`), modelled as a float that falls every frame — it runs on the same sailing hours the wind and the rowers' stamina do, so fast-forward sours the crew at the rate it eats the calendar |
| calling at a city | **+10%** (`CityCall`), any city, any size — the moment the hull comes alongside and the market opens (`src/trading/Docking.cs`). Clicking a city's name from out at sea is not calling there and pays nothing |
| a pub night | **morale full**, medium and large ports only — see below |
| a cannonball in the hull | **−5%** a ball (`HitTaken`), counted off the player boat's `HitsTaken` |
| sinking somebody | **+5%** a hull (`KillMade`), off the same `OnSunk` hook that pays the honour ([ai.md](ai.md)) |

Nothing can push it outside 0..1.

## What it costs

Below half, the crew work the boat slower — and only below half:

| morale | speed |
|---|---|
| 50% and up | 100% |
| 25% | 75% |
| 0% | 50% |

`CrewMorale.SpeedFactor` is that straight line: full at `FullSpeedLevel`
(0.5) and above, falling to `MinSpeedFactor` (0.5) at nothing left. `Main`
feeds it to `Boat.SpeedScale` every frame, and the boat caps **the way she
has on the water** at that fraction of `BoatConfig.TerminalSpeed()` for her
sail state ([boat.md](boat.md)). The cap is on the speed rather than on the
thrust deliberately: it then holds whatever is pushing her — canvas, oars,
or the current under her — which is what the rule is written about. AI boats
leave `SpeedScale` at 1 and are unaffected.

## Pub night

Medium and large cities (the same ports that have a harbour and a bank —
`CrewMorale.HasPub`) carry a **Pub night** row in the market panel. Striking
it turns the crew loose ashore: the clock runs on to **08:00 the next
morning**, or the morning after that if they are still up past **03:00**
(`PubNightMinutes`, so a night out started at 3am costs 29 hours), and they
come back aboard with their morale **full**.

Nothing else is skipped over. Rations, wages, loan due dates, the story
events and the markets all read the game clock, so they catch up by
themselves over the frames that follow; the wind is told the hours directly,
since its swing runs on hours accumulated rather than on the calendar.

## Reading it and testing it

The bar sits directly under the stamina bar, top centre: `MORALE 42%`, plus
`SPEED 85%` in red once the crew are dragging their feet
([ui.md](ui.md)). The HUD's sailing line carries the same two numbers, and a
scripted run's final line and `--json` both report `morale` (and
`moraleSpeed` in the JSON).

Test hooks: `--morale=0..1` starts the crew at a level, and `--pub` spends
the first frame in the pub of a medium or large city, which is how the jump
in the clock is asserted on:

```bash
./sim.sh --seed=7 --frames=5 --pub --json          # minutes jumps ~24h, morale 1
./sim.sh --seed=7 --frames=2400 --start=5330,2763,180 --morale=0 --json   # 2/3 the ground covered
./sim.sh --scenario=gunnery --frames=3000 --fire=5,0@40,160,280,400 --json  # sink her: morale up 5%
```

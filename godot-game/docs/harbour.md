# Harbours: buying a boat

Files: `boats.yml` (the fleet), `src/boat/BoatClass.cs` (what a vessel is,
and the parser), `src/harbour/Harbour.cs` (the deal, Godot-free and tested),
the Harbour section of `src/ui/CityPopup.cs`, and `Main.BuyBoat` (which does
the swap). Nothing here knows how a boat sails; nothing in
[boat.md](boat.md) knows what one costs.

## The fleet is a file

Every vessel in the game is a block in `boats.yml`, read once at launch the
way `game-config.yml` is. Add a block and the boat exists: it appears in the
harbours, gets a model with that many masts, and is armed from its own gun
counts. Every key is required — the file, not the code, is where a boat's
numbers live.

| key | |
|---|---|
| `name` | what it is called, everywhere it is shown |
| `hullLength`, `hullWidth` | world units. The model, the blob shadow, the hit box and the collision shell all measure off these ([boat.md](boat.md)) |
| `sails` | masts on the model |
| `crew` | fixed by the hull — rations and wages follow it ([trading.md](trading.md)) |
| `cargo` | units the hold takes |
| `cost` | buy and sell price alike |
| `sailThrust`, `oarThrust`, `turn` | **multipliers** over `BoatConfig`'s `SailThrustScale` / `OarThrustScale` / `TurnRate` |
| `gunsBow`, `gunsStern`, `gunsPerSide` | the battery, laid out by `CombatConfig.MountsFor` ([combat.md](combat.md)) |

Handling is a multiplier and never an absolute, so the `[Tweak]` sliders stay
the one place the boat's feel is tuned and moving one moves the whole fleet
together. **`medium` is all `1.0`: it is the boat the game has always had,
down to the last decimal, and a scripted run proves it.** Two names are
load-bearing (`BoatClasses.StartingName` / `AiName`): the player starts in
the `small` boat and every AI hull is a `medium`, so the file must contain
both or the game will not launch.

The three that ship:

| | small | medium | large |
|---|---|---|---|
| cost | 1000 | 10000 | 80000 |
| crew | 1 | 10 | 30 |
| hold | 200 | 1200 | 10000 |
| guns | 1 | 7 | 16 |
| masts | 1 | 2 | 3 |
| under canvas | slower | — | **faster** |
| at the oars | slower | — | **slowest** |
| helm | handiest | — | slowest |

The large boat is the fast one at sea and a slug in a calm: 30 hands cannot
row 10,000 units of hold anywhere in a hurry. Measured over 600 frames dead
downwind: 73.6 / 80.3 / 88.2 u/s under canvas, 38.4 / 43.0 / 33.5 at the
oars, and 95° / 82° / 70° of heading in a 200-frame turn.

## The deal

Medium and large cities have a harbour (`Harbour.HasHarbour`); a small one
has a market and nothing else. **You own exactly one hull**, so every
purchase is a trade: the boat you are in goes back over the counter at what
it cost — buy and sell price are the same — and only the difference changes
hands. Trading down therefore pays.

`Harbour.OfferOf` prices one row and says whether it can be struck:

- the boat you are already sailing is listed, marked `◆ YOURS`, and cannot be bought;
- a hull whose hold is smaller than the cargo aboard is **refused**, not
  emptied — "sell down to 200 units first". Nothing is ever thrown overboard
  behind the player's back;
- the purse has to cover the *difference*, not the price: "you are 1 short".

A refused row still shows its price and its numbers with the reason in the
tooltip. Knowing the large boat exists, and what she would cost, is half the
point of walking down to the water.

## The swap

`Main.BuyBoat` takes the difference out of the purse and then the *same node*
becomes the new vessel: `Boat.SetClass` rebuilds the model and the hull's
size, and the battery is rebuilt from the new gun counts. She keeps her
place, her way, her heading and her canvas, so no camera, docking or combat
state has to be re-established. `Player.Boat` is where the hold and the crew
come from — `CrewCount` and `CargoCapacity()` are the boat's, not the
player's.

`--buy=name` strikes the deal from a scripted run, topping the purse up to
cover it (a bench should not have to sail a trade route first) but refusing
a hold too full exactly as the popup does. `--boat=name` starts in one
instead, and a scenario names its own with `boat:` ([scenarios.md](scenarios.md)).

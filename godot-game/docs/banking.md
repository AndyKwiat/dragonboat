# Banking: loans, interest, late penalties

Files: `src/banking/Loans.cs` (all the rules, no Godot types — unit-tested in
`tests/LoansTests.cs`), `src/ui/BankPopup.cs` (the bank window),
`src/ui/EventLog.cs` (the world-news feed), the `// Banking` and
`// Event log` blocks of `src/core/Config.cs`, plus the bank row in
`src/ui/CityPopup.cs`, the loan lines in `src/ui/Clock.cs` and the debt tag
in `src/ui/CityLabels.cs`. Wired in `Main.cs`.

Banking never touches trading or combat: it moves the player's money and
honour and reads the clock, nothing else.

## Where the banks are

Medium and large cities have a bank (`Loans.HasBank`); small ones do not.
A city's market popup grows a **Bank** heading with a one-line statement and
a Bank button; the button opens `BankPopup` — a second, narrower panel
centred *over* the market panel. Esc (or a click off it) closes the bank
window first and leaves the market open underneath; Esc again closes the
market.

## Borrowing

- **Credit** = honour × `Bank.HonourMultiplier` (10), so 100 honour is a
  1000 limit, capped by the city's own ceiling: `MaxMedium` (5000) or
  `MaxLarge` (50000). It is rounded down to a whole `Step` (100), which is
  also the granularity the window's slider offers. No honour, no credit —
  and honour may be negative, which is the same as none.
- **One loan per city at a time.** Each bank keeps its own books; you can
  owe several cities at once. A city that already lent you money offers a
  credit limit of 0 until it is paid back.
- **Repayment** is the principal plus `Bank.Interest` (20%), rounded up
  (`Loans.OwedFor`), and it falls due `Bank.TermDays` (30) days later.
  There are no instalments: the bank window's Repay button is enabled only
  when the whole sum is aboard, and only in the city that lent it.
- **Paying up on time earns honour**: principal ÷
  `Bank.OnTimeHonourDivisor` (50), so a 2000 loan settled on or before its
  due day is worth +40 honour (`Loans.OnTimeHonourFor`, banked by
  `Loans.Repay`, which returns it). A day late and it is worth nothing —
  by then the penalties below are running instead. Borrowing and clearing
  loans is therefore a way to *build* credit, not just spend it.

## Falling behind

Everything is counted in whole days — `Days.Of(clock minutes)`,
`src/core/News.cs` — because loans fall due at a midnight and the penalties
are charged at midnights too (VIP passengers run on the same beat, behind
the same `DayTurn` gate).
`Loans.Update(today, loseHonour)` is called every frame from `Main` and is a
single integer compare until the day turns over, like the story events'
midnight pass.

- **Warnings**: from `Bank.WarnDays` (3) days out, each midnight posts
  "Loan coming due in *city* in 3 days!!" to the event log; on the due day
  itself, "Loan due TODAY in *city*".
- **Penalties**: one day late costs honour equal to the **principal**
  divided by `Bank.LatePenaltyDivisor` (10) — a 1000 loan costs 100 honour —
  and the same again at 5, 10, 15… days late (`Bank.LatePenaltyEveryDays`),
  until it is repaid. Honour goes negative freely (and negative honour means
  no more credit anywhere). A clock that jumped several days charges every
  penalty it skipped in one pass. Each posts a red event-log line.
- The debt itself never grows; only the honour bill does — and a late loan
  forfeits the on-time honour reward as well.

## Where the player sees it

- **Clock overlay** (top left, under the VIP lines): one line per
  outstanding loan — `BANK LOAN $12K - CINDERFALL (due in 11d)`, the amount
  already including the interest, turning red and reading `(3d OVERDUE)`
  once it is late. Rebuilt only when a loan changes or the day turns.
- **City labels**: under the name and the price intel, `◆ OWE $12K (11d)` —
  amber, red and `LATE` past the due date — so sailing past a city tells you
  it is waiting for its money (`CityLabels.RefreshIntel`).
- **Event log** (bottom left, `ui/EventLog.cs`): world news while you sail —
  the warnings and penalties above, plus a line when you borrow or repay.
  Lines fade after `Config.EventLogSeconds` (14 real seconds), oldest first
  past `EventLogMaxLines`.
- **The market panel's Bank row**: what this city's bank is owed and when,
  or what it would lend you today. The bank window itself shows what
  settling in time is worth, on the account line and the Repay button.

## Test hooks

Scripted runs (`Main.ParseArgs`):

| flag | effect |
|---|---|
| `--honour=N` | set the player's honour, which is what credit is derived from |
| `--loan=N` | borrow N from the start city's bank (or `--city=`'s) at launch |
| `--loan=N@D` | …with the due date moved D days earlier — `@4` is four days late (negative is the other way: `@-2` falls due in two days), and the day pass runs on the first frame so the penalty or warning lands immediately |
| `--bank` | open that city's bank window over its market |

```bash
$G --path . -- --screenshot=x.png --seed=7 --honour=800 --bank            # the bank window
$G --path . -- --screenshot=x.png --seed=7 --honour=800 --loan=8000       # HUD line + debt tag
$G --path . -- --screenshot=x.png --seed=7 --honour=800 --loan=1000@4     # late: honour -100 in the event log
$G --path . -- --screenshot=x.png --seed=7 --honour=800 --loan=2000@-2    # "Loan coming due ... in 2 days!!"
```

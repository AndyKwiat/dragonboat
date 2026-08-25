using System;

// When running the hull into a city opens its market (Main feeds it the city
// the boat is touching each frame; CityPlacer.Touching decides that). Two
// rules keep the market from snapping open again the moment you close it:
//
//   1. You must leave. The city whose market was open while the hull was
//      against it is remembered until the hull is clear of it, and it can't
//      reopen before then — whether the market was opened by the collision
//      or by clicking the city's label.
//   2. A cooldown runs from every close, so bumping straight back into the
//      same quay — or drifting into the one next door — does nothing until
//      it lapses.
//
// No Godot types: the rules are unit-tested in tests/.
public sealed class Docking
{
    private readonly float cooldownSec;
    private City? docked;      // the city whose market we opened, until we leave it
    private bool wasOpen;

    public float CooldownLeft { get; private set; }
    public City? DockedAt => docked;

    public Docking(float cooldownSec) => this.cooldownSec = cooldownSec;

    // Call once a frame with real dt, the city the hull is against (null if
    // none) and whether the market panel is open. Returns the city whose
    // market should open now, or null.
    public City? Update(float dt, City? atQuay, bool marketOpen)
    {
        CooldownLeft = Math.Max(0, CooldownLeft - dt);
        if (atQuay == null) docked = null;      // clear of the quay: dockable again
        // A market opened by clicking the city's label counts as having
        // docked here, so closing it doesn't let the hull's contact reopen it
        // once the cooldown lapses — you still have to leave first.
        else if (marketOpen) docked = atQuay;
        City? open = null;
        if (!marketOpen && atQuay != null && atQuay != docked && CooldownLeft <= 0)
        {
            docked = atQuay;
            open = atQuay;
        }
        // A market that just closed starts the cooldown — however it closed
        // (Esc, the ✕ button, or clicking off it).
        if (wasOpen && !marketOpen) CooldownLeft = cooldownSec;
        wasOpen = marketOpen || open != null;
        return open;
    }
}

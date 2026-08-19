# combat-prototype

Standalone boat-combat sandbox: one HTML file, three.js from a CDN, no build.

```
./run.sh          # starts http://localhost:8000, opens the browser, live-reloads on save
./run.sh 9000     # different port
```

Controls: WASD to sail, click the water to fire the bow cannon. Tweak values with
the sliders (top right) or edit the `P` object at the top of `index.html`.

**Ported:** the boat model, lean, cannons, ballistics, impact effects and the
tweak panel now live in `../godot-game` (`src/boat/`, `src/combat/`,
`src/ui/TweakPanel.cs`; see `../godot-game/docs/combat.md`). This sandbox
stays as the quick place to try ideas; port anything worth keeping over.

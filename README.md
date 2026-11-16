# dragonboat

A sailing, trading and naval-combat game, built in Godot 4 (.NET / C#). You sail a two-masted boat across a hand-painted terrain map between cities: buy and sell cargo in a supply-driven economy, feed and pay your crew, borrow from city banks, upgrade to a bigger boat, and fight AI boats with cannons.

The repo has three parts:

- **`godot-game/`**: the game itself. See its README for build and run scripts and the per-system design docs in `docs/`.
- **`terrain-gen/`**: a browser-based terrain painter (Three.js + Bun) for making the maps the game loads.
- **`combat-prototype/`**: the original single-file three.js boat-combat sandbox. Its mechanics have since been ported into the game.

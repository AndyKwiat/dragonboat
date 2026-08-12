# Decisions

## 2026-08-12 — Engine: Godot 4 + C#

We prototyped the same voxel demo (256×256 procedural world, 15,000 wandering
agents, fixed isometric camera) four ways: Rust/Bevy (native + WASM/WebGPU),
three.js/WebGL2, Godot/GDScript, and Godot/C# (`rust_demo/` and its
subfolders). Godot-C# won on the balance of simulation performance and
iteration speed: ~477–494 FPS uncapped at 15k movers (~4× GDScript, whose
interpreter burned ~8.5 ms/frame in the mover loop; Bevy's browser build
managed ~84–98 even after instancing work), with a 0.6 s incremental
`dotnet build` — effectively the same edit-to-running loop as GDScript and
~5× faster than the Rust/WASM cycle. three.js posted the highest raw FPS
(~660–710) and the fastest iteration (~0.5 s), but the gap over Godot-C# is
engine overhead we get real features for (editor, scene tools, profiler,
live inspection), and C#'s managed stack traces plus Godot's remote scene
debugging make runtime bugs far cheaper to chase than in Rust/WASM, where we
had to strip debuginfo just to keep builds fast.

The tiebreaker was distribution: the target is a paid Steam release, and
Godot exports a normal native binary — Steam overlay, Steam Input, and Deck
verification work routinely, with mature Steamworks bindings (GodotSteam /
Facepunch.Steamworks). The three.js path requires Electron plus
community-maintained native bindings, carries known Steam-overlay and
packaging pain (~200 MB downloads, notarization), and forecloses consoles
without a rewrite. Costs accepted with eyes open: the C# web export is
weak/lagging (browser demos would need a separate small web build), C# has a
few seconds of JIT warm-up at startup, and console ports would go through
third-party porters. Working baseline: `rust_demo/godot_cs_test/` (Godot
4.7.1 .NET build at /Applications/Godot_mono.app, .NET SDK 8+); its README
documents the measurement methodology and the headless `--screenshot`
verification flow for agents.

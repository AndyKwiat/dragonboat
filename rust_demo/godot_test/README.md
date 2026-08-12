# Godot comparison port

A GDScript/Godot 4.7 port of the Rust/Bevy voxel demo in the parent folder —
same seed (12345), same SplitMix64/value-noise terrain (u64 math via GDScript's
wrapping 64-bit ints; logical shifts emulated with masks), same 256×256 world,
15,000 movers with identical wander rules, 220/60/180 trees/rocks/houses, same
fixed orthographic camera. Everything is built programmatically in one script
(`main.gd`); `main.tscn` is just a root node.

Movers render as 5 `MultiMesh`es (one per palette color, 3,000 instances
each), the analog of the three.js `InstancedMesh` / Bevy auto-instancing
setups. Terrain is 256 vertex-colored `ArrayMesh` chunks.

## Run

```bash
# The .NET build is the only Godot installed; it runs GDScript projects too.
/Applications/Godot_mono.app/Contents/MacOS/Godot --path .
```

No build step — edit `main.gd` and re-run (~1.7 s to a live scene).

Agent-friendly verification without a visible window:

```bash
# parse/runtime check, no rendering:
Godot --path . --headless --quit-after 5
# render 4s, save screenshot.png, quit:
Godot --path . -- --screenshot
```

## Measured (2026-08, M-series Mac, 1000×1000, 15,000 movers)

| | FPS uncapped | Edit → running |
|---|---|---|
| Godot 4.7 (GDScript, Metal/forward+) | ~117–118 | ~1.7 s |
| Rust/Bevy native (Metal) | ≥120 (vsync-capped; uncapped unreliable off-screen) | ~3 s |
| Rust/Bevy browser (WASM/WebGPU) | ~84–98 | ~10 s |
| three.js (WebGL2) | ~660–710 | ~0.5 s |

The Godot frame time (~8.5 ms) is dominated by the GDScript `_move_movers`
loop (15k iterations + 15k `set_instance_transform` calls per frame), not the
GPU.

**Tried and rejected:** replacing the per-instance calls with a
`MultiMesh.buffer` bulk upload (12 floats per mover written into a
`PackedFloat32Array`, one buffer assignment per variant). Measured **slower**
— ~98 FPS vs ~117 — because 12 interpreter-dispatched array stores per mover
cost more than one native `set_instance_transform` call. The bulk-buffer
pattern only pays off when the buffer is built natively (C# or GDExtension);
in pure GDScript, minimizing VM operations per mover is what matters. Porting
the loop to C# remains the real next lever.

## Gotchas found while porting (for future agents)

- Godot front faces wind **clockwise** — quad indices are reversed vs
  Bevy/three.js, or faces render black from the intended viewing side.
- GDScript int literals must fit signed 64-bit; the u64 hash constants are
  written as their two's-complement signed equivalents.
- `%` keeps the dividend's sign; use the `umod()` helper for u64 modulo.
- `>>` is arithmetic; use the `lshr()` helper for u64 semantics.

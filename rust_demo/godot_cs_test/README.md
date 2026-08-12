# Godot C# comparison port

The same voxel demo as `../godot_test`, ported entirely to C# (one `Main.cs`).
Same seed (12345), same SplitMix64/value-noise terrain (real `ulong` math —
none of the GDScript signed-wrapping workarounds), same 256×256 world, 15,000
movers, 220/60/180 trees/rocks/houses, same fixed orthographic camera.

Movers use the `MultiMesh.Buffer` bulk upload (5 float[] buffers, one per
palette variant) — the pattern that measured *slower* in GDScript pays off
here because C# fills the buffers at native speed.

## Requirements

- **Godot 4.7 .NET build** — `/Applications/Godot_mono.app` (the regular
  build cannot run C#).
- A .NET SDK (8+).

## Run

```bash
dotnet build        # after any code change, ~0.6 s incremental
/Applications/Godot_mono.app/Contents/MacOS/Godot --path .
```

Headless verification (same flags as the GDScript port):

```bash
Godot --path . --headless --quit-after 5     # parse/runtime check
Godot --path . -- --screenshot               # render 4 s, save screenshot.png, quit
```

## Measured (2026-08, M-series Mac, 1000×1000, 15,000 movers)

| | FPS uncapped | Sim loop cost | Edit → running |
|---|---|---|---|
| **Godot 4.7 C#** (this) | **~477–494** | ~1.5 ms | ~2 s (0.6 s build) |
| Godot 4.7 GDScript | ~117–118 | ~8.5 ms | ~1.7 s |
| three.js (WebGL2) | ~660–710 | ~1.5 ms | ~0.5 s |
| Rust/Bevy native (Metal) | ≥120 (vsync-capped) | — | ~3 s |
| Rust/Bevy browser (WASM/WebGPU) | ~84–98 | — | ~10 s |

C# is ~4× GDScript on the same engine and scene: the entire difference is the
15k-mover simulation loop (interpreter vs JIT). C# takes a few seconds of JIT
warm-up (~82 FPS for the first seconds, then steady ~490). Iteration speed is
effectively the same as GDScript once the 0.6 s incremental build is paid.

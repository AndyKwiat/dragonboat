# Instructions for Coding Agents

This repository is a Rust/Bevy browser voxel demo.

Before modifying anything:
1. Read README.md.
2. Read docs/ARCHITECTURE.md.
3. Read src/config.rs.
4. Preserve browser WASM support.
5. Run the verification commands below before declaring work complete.

## Facts

- **Bevy version:** 0.19 (`bevy = "0.19"` in Cargo.toml).
- **Primary target:** browser, `wasm32-unknown-unknown`, **WebGPU** via the
  crate feature `webgpu` (which enables `bevy/webgpu`). Do not silently switch
  to WebGL2.
- **Native build** (`cargo run`) must keep working; there is no
  platform-specific game logic.
- **Goal:** a fixed-camera isometric voxel scene proving Rust→WASM→WebGPU
  viability at strategy-game scale. It is a demo, not a game.

## Conventions

- **Coordinates:** X/Z is the ground plane, Y is up. 1 voxel = 1.0 world unit.
  World spans x ∈ [0, 256), z ∈ [0, 256). Cell (x, z) occupies world
  [x, x+1) × [z, z+1); its top surface is at y = height.
- **Camera:** fixed orthographic, azimuth 45°, elevation 38°, framed by
  `ScalingMode::FixedVertical` so browser resizing keeps the same world
  framing. Never add user camera controls by default.
- **Terrain data:** `WorldGrid` resource (`src/world.rs`) — flat
  `Vec<TerrainCell>` indexed `z * WORLD_WIDTH + x`. Each cell: height (u8),
  terrain type, color variant. Water cells store the *water surface* height.
- **Chunking:** `src/terrain.rs` builds one mesh per 16 × 16 chunk: top quad
  per cell + side quads only where a neighbor is lower. No bottom faces, no
  internal faces. Per-vertex colors, one shared white material.
- **Movers:** `src/movers.rs`. One parent entity per mover (heading/speed/rng
  state in the `Mover` component) with two child cubes (body + head) sharing
  meshes and a 5-color material palette. Movement is deterministic wandering:
  bounce at edges, reverse at water, follow `WorldGrid::height_at`.
- **Randomness:** fully deterministic — SplitMix64 hashing seeded from
  `WORLD_SEED`. No `rand` crate, no `getrandom` (this keeps wasm builds
  simple). Keep it that way.
- **Configuration:** all tuning constants live in `src/config.rs`.

## File ownership

| File | Owns |
|---|---|
| `src/main.rs` | plugin composition + window/canvas setup only |
| `src/config.rs` | every tuning constant |
| `src/world.rs` | terrain generation, `WorldGrid`, height lookup |
| `src/terrain.rs` | chunk meshing, terrain palette, static decorations |
| `src/movers.rs` | mover spawning + movement systems |
| `src/camera.rs` | camera + lights |
| `src/diagnostics.rs` | FPS overlay |
| `web/index.html` | browser shell (served via `WASM_SERVER_RUNNER_CUSTOM_INDEX_HTML`) |

## Performance-sensitive decisions (do not casually refactor)

- Terrain is ~256 chunk meshes, **not** 65k cube entities.
- Hidden terrain faces are never generated.
- Movers share mesh/material handles; the movement loop allocates nothing.
- Per-entity RNG is a `u64` state stepped inline (no allocation).

## Commands

```bash
# develop (browser, WebGPU) — serves on http://127.0.0.1:1334
./run-web.sh

# develop (native window)
cargo run

# verify before declaring done
cargo fmt --check
cargo check
cargo check --target wasm32-unknown-unknown --features webgpu
cargo clippy --all-targets -- -D warnings
cargo clippy --target wasm32-unknown-unknown --features webgpu -- -D warnings

# release wasm build
cargo build --release --target wasm32-unknown-unknown --features webgpu
```

Note: `--all-features` is avoided because `webgpu` is wasm-oriented; clippy is
run per-target with the correct feature set instead.

## Known browser limitations

- Requires WebGPU (`navigator.gpu`); the page shows a fallback message otherwise.
- No filesystem/threads assumptions — keep it that way.
- Dev wasm builds are large (tens of MB); that is expected. Use the release
  profile for size.

## Rules

> Any agent making a meaningful architectural change must update `AGENTS.md`
> and `docs/ARCHITECTURE.md` in the same change.

> Adding a new major subsystem requires documenting it in
> `docs/ARCHITECTURE.md` and, if it has knobs, `src/config.rs` + `docs/TUNING.md`.

> Never declare success without actually launching the wasm build in a browser.

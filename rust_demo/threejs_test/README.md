# three.js comparison port

A line-for-line JavaScript/three.js port of the Rust/Bevy voxel demo in the
parent folder, used to compare FPS and iteration speed. Same seed (12345),
same SplitMix64/value-noise terrain (u64 math via BigInt), same 256×256 world,
same 15,000 movers with identical wander rules, same 220/60/180
trees/rocks/houses, same fixed orthographic camera.

Implementation difference worth knowing: movers here use two
`THREE.InstancedMesh` draws with a hand-rolled typed-array update loop
(idiomatic three.js), while the Bevy version spawns individual ECS entities
(idiomatic Bevy). Renderer is WebGL2, not WebGPU.

## Run

```bash
npm install        # once, installs three
python3 -m http.server 8017   # from this folder — then open http://127.0.0.1:8017/
```

No build step: edit `main.js`, refresh the browser.

## Measured (2026-08, M-series Mac, headless Chrome 1000×1000, 15,000 movers)

| | Rust/Bevy (WebGPU) | three.js (WebGL2) |
|---|---|---|
| FPS, uncapped, original (45k entities) | ~63–68 | — |
| FPS, uncapped, instanced movers | ~84–98 | ~660–710 |
| FPS, normal vsync display | ~95 (below cap) | 120 (display cap) |
| Edit → running new code | ~6–10 s | ~0.5 s (refresh) |

(The three.js copy was later doubled to 30,000 movers, where it measures
~320–430 FPS uncapped.)

See the conversation/commit history for methodology.

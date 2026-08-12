# The web build, in plain English

```text
Rust source
    |
    v
rustc
target: wasm32-unknown-unknown
    |
    v
.wasm
    |
    v
browser
    |
    v
Bevy / wgpu
    |
    v
WebGPU
    |
    v
GPU
```

- Rust compiles into WebAssembly (`wasm32-unknown-unknown`).
- The WASM module executes inside the browser's sandbox.
- Bevy's rendering stack (wgpu) talks to the browser's **WebGPU** API —
  enabled by building with the crate feature `webgpu`, which turns on
  `bevy/webgpu`. Bevy's `webgpu` feature takes precedence over its default
  `webgl2` feature, so the build renders with WebGPU, not WebGL2.
- The webpage (`web/index.html`) is only a host/shell: a canvas, a loading
  message, and a WebGPU fallback notice. All game/simulation logic is Rust.

## Development

```bash
./run-web.sh
# equivalent to:
WASM_SERVER_RUNNER_CUSTOM_INDEX_HTML=$PWD/web/index.html \
  cargo run --target wasm32-unknown-unknown --features webgpu
```

`.cargo/config.toml` sets `wasm-server-runner` as the runner for the wasm
target, so `cargo run` builds the module, runs wasm-bindgen on it, and serves
it at **http://127.0.0.1:1334**. The dev page auto-reloads when you rebuild,
and forwards browser console output back to the terminal.

Install prerequisites once:

```bash
rustup target add wasm32-unknown-unknown
cargo install wasm-server-runner
```

## Release

```bash
cargo build --release --target wasm32-unknown-unknown --features webgpu
```

Then run wasm-bindgen yourself (or serve via
`cargo run --release --target wasm32-unknown-unknown --features webgpu`).
The release profile (`opt-level = "s"`, LTO, 1 codegen unit) substantially
shrinks the .wasm; dev builds are intentionally large and fast to produce.

## Native

```bash
cargo run
```

Same code; wgpu picks Metal/Vulkan/DX12 automatically. The `webgpu` feature is
only meaningful for wasm builds.

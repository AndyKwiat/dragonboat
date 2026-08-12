#!/usr/bin/env bash
# Build and serve the browser (WASM + WebGPU) version of the demo.
# Requires: rustup target add wasm32-unknown-unknown && cargo install wasm-server-runner
set -euo pipefail
cd "$(dirname "$0")"

if ! rustup target list --installed 2>/dev/null | grep -q wasm32-unknown-unknown; then
    echo "Missing wasm target. Run: rustup target add wasm32-unknown-unknown" >&2
    exit 1
fi
if ! command -v wasm-server-runner >/dev/null; then
    echo "Missing wasm-server-runner. Run: cargo install wasm-server-runner" >&2
    exit 1
fi

# Serve our custom dark page + WebGPU fallback message instead of the
# default wasm-server-runner index.
export WASM_SERVER_RUNNER_CUSTOM_INDEX_HTML="$PWD/web/index.html"

echo "Building for wasm32-unknown-unknown (WebGPU)..."
echo "When the server starts, open the printed URL (usually http://127.0.0.1:1334)."
exec cargo run --target wasm32-unknown-unknown --features webgpu "$@"

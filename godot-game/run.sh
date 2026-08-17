#!/bin/sh
# Run the game (build first with ./build.sh after code changes).
# Extra args are passed to the game, e.g. ./run.sh -- --screenshot=shot.png
cd "$(dirname "$0")" && exec /Applications/Godot_mono.app/Contents/MacOS/Godot --path . "$@"

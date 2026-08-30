#!/bin/sh
# Run the pathing test scenario instead of the game: the pathingTest map, no
# cities, no currents, no hostile fleet, the player on marker p0 and an AI
# boat on p1 and p2 (scenarios/pathing.yml, docs/scenarios.md).
#
# Build first with ./build.sh, as always. Unlike ./run.sh, extra args need no
# `--` — they go straight to the game:
#   ./pathingTest.sh --screenshot=x.png --frames=120 --mode=free --zoom=1
cd "$(dirname "$0")" && exec /Applications/Godot_mono.app/Contents/MacOS/Godot --path . -- --scenario=pathing "$@"

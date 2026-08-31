#!/bin/sh
# Run the battle test scenario instead of the game: the battleTest map, no
# cities, no currents, no legacy fleet, the player on marker 'player' and one
# AI boat on 'ai0' (scenarios/battle.yml, docs/scenarios.md).
#
# Build first with ./build.sh, as always. Like ./pathingTest.sh and unlike
# ./run.sh, extra args need no `--` — they go straight to the game:
#   ./battleTest.sh --screenshot=x.png --frames=120 --mode=free --center=210,175
cd "$(dirname "$0")" && exec /Applications/Godot_mono.app/Contents/MacOS/Godot --path . -- --scenario=battle "$@"

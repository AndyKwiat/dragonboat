#!/bin/sh
# Run the simulation with no window and no rendering, print the report, quit.
# This is the fast way to check something.
#
#   ./sim.sh --scenario=battle --frames=1200 --json
#   ./sim.sh --seed=7 --frames=600                 # the real world, as a line
#
# Three things make it fast and repeatable: --headless draws nothing,
# --fixed-fps takes the main loop off real-time pacing (without it the
# headless loop still sleeps to about 140 fps, and 6000 frames costs 42
# seconds instead of 0.7), and the game steps by a fixed amount of game time
# per frame, so frame N is the same moment in the world on every run and
# every machine.
#
# Needs --frames, or it would never end. Extra args need no `--`, as with
# battleTest.sh. For a picture use ./run.sh — nothing is drawn here.
cd "$(dirname "$0")" \
  && exec /Applications/Godot_mono.app/Contents/MacOS/Godot --headless --fixed-fps 120 --path . -- "$@"

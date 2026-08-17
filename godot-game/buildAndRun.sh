#!/bin/sh
# Compile, then run (extra args go to the game).
cd "$(dirname "$0")" && ./build.sh && exec ./run.sh "$@"

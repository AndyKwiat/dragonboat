#!/bin/sh
# Compile the C# project.
cd "$(dirname "$0")" && exec dotnet build

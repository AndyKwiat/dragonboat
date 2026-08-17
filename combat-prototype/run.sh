#!/bin/sh
# Start the dev server and open the prototype in the browser.
cd "$(dirname "$0")" && exec node serve.js "$@"

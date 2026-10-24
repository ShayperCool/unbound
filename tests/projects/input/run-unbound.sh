#!/bin/sh
# Plays the input probe in Unbound with the steps of an Editor record and writes Unbound's record:
#   run-unbound.sh REFERENCE OUT [UNBOUND] [extra unbound run options...]
# UNBOUND defaults to `unbound` on PATH. Headless at 960x540, 60 fixed frames per second.
set -e
here=$(dirname "$0")
reference=$1
out=$2
unbound=${3:-unbound}
shift 3 2>/dev/null || shift $#
args=$(python3 "$here/compare.py" args "$reference")
# shellcheck disable=SC2086
INPUT_PROBE_OUT=$out "$unbound" run -p "$here" --headless 310 --size 960x540 --fixed-fps 60 --no-watch $args "$@"

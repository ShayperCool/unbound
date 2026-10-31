#!/usr/bin/env bash
# Run from the repository root with an engine CLI and managed build selected.
set -euo pipefail
export MOTION_WORK="${MOTION_WORK:-${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/physics2dmotion/current}"
mkdir -p "$MOTION_WORK"
for label in native1 native2; do
    python3 tests/projects/physics2dmotion/run.py prepare --label "$label"
    python3 tests/projects/physics2dmotion/run.py native --label "$label"
done
cmp "$MOTION_WORK/native1.json" "$MOTION_WORK/native2.json"
python3 tests/projects/physics2dmotion/verify.py --suite motion --candidate "$MOTION_WORK/native2.json"
MOTION_PROJECT=project-native1 python3 tests/projects/physics2dmotion/run.py engine --label baseline
# Build/select the candidate managed runtime before capturing final.
MOTION_PROJECT=project-native1 python3 tests/projects/physics2dmotion/run.py engine --label final
python3 tests/projects/physics2dmotion/report.py final --baseline baseline
python3 tests/projects/physics2dmotion/verify.py --suite motion --candidate "$MOTION_WORK/final.json" --baseline "$MOTION_WORK/baseline.json"

#!/usr/bin/env bash
set -euo pipefail
: "${LOADING_WORK:?source the private env.sh first}"
export TRIGGER_WORK="$LOADING_WORK/validation/engine"
export SCENE_WORK="$TRIGGER_WORK/work"
export SCENE_PARENT_WORK="$TRIGGER_WORK/probes"
mkdir -p "$LOADING_WORK/validation"
python3 tests/projects/inactive3dloading/validate.py

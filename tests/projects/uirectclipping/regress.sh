#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../../.."
export PATH="$HOME/.dotnet:$HOME/.cargo/bin:$PATH"
export DISPLAY=:0 XAUTHORITY="$(ls -t /run/user/1000/.mutter-Xwaylandauth.* | head -1)"
export UseSharedCompilation=false MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
export LD_LIBRARY_PATH="/mnt/storage-hdd/node-b-caches-20261023/unialt-target-compat/managed:${LD_LIBRARY_PATH:-}"
export UNBOUND_MANAGED_DIR="$HOME/.cache/compat/rectclip-${2:-managed}"
heavy="$HOME/.cache/compat/with-heavy-job"
u=/mnt/storage-hdd/node-b-caches-20261023/unialt-target-compat/release/unbound
out="$HOME/.cache/compat/rectclip-${1:-after}"
mkdir -p "$out"
UITK_PROBE_OUT="$out/uitk/unbound.jsonl" "$heavy" timeout 300 "$u" run -p "$PWD/tests/projects/uitk" --frames 35 --size 960x540 --no-watch > "$out/uitk.log" 2>&1
if [ "${1:-after}" = before ]; then exit; fi
UI_COLORS_OUT="$out/colors-linear" "$heavy" timeout 300 "$u" run -p "$PWD/tests/projects/uicolors" --frames 8 --size 128x128 --no-watch > "$out/colors-linear.log" 2>&1
python3 - <<'PY'
from pathlib import Path
import shutil
p=Path('tests/projects/uirectclipping/Library/ColorRegression')
for part in ['Assets','Packages','ProjectSettings']: shutil.copytree(Path('tests/projects/uicolors')/part,p/part,dirs_exist_ok=True)
s=p/'ProjectSettings/ProjectSettings.asset';s.write_text(s.read_text().replace('m_ActiveColorSpace: 1','m_ActiveColorSpace: 0'))
PY
UI_COLORS_OUT="$out/colors-gamma" "$heavy" timeout 300 "$u" run -p "$PWD/tests/projects/uirectclipping/Library/ColorRegression" --frames 8 --size 128x128 --no-watch > "$out/colors-gamma.log" 2>&1
CANVAS_LIFECYCLE_OUT="$out/lifecycle" "$heavy" timeout 300 "$u" run -p "$PWD/tests/projects/canvaslifecycle" --frames 24 --size 640x480 --no-watch > "$out/lifecycle.log" 2>&1
UITRANSITIONS_OUT="$out/transitions" "$heavy" timeout 300 "$u" run -p "$PWD/tests/projects/uitransitions" --headless 100 --fixed-fps 60 > "$out/transitions.log" 2>&1
python3 tests/projects/uicolors/compare.py tests/projects/uicolors/Reference/Linear "$out/colors-linear" -q
python3 tests/projects/uicolors/compare.py tests/projects/uicolors/Reference/Gamma "$out/colors-gamma" -q
python3 tests/projects/canvaslifecycle/compare.py tests/projects/canvaslifecycle/Reference "$out/lifecycle"
python3 tests/projects/uitransitions/compare.py tests/projects/uitransitions/Reference/transitions.jsonl "$out/transitions/transitions.jsonl"
python3 tests/projects/uitk/compare.py "$HOME/.cache/compat/rectclip-before/uitk/unbound.jsonl" "$out/uitk/unbound.jsonl"

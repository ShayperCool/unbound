#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../../.."
export PATH="$HOME/.dotnet:$HOME/.cargo/bin:$PATH" UseSharedCompilation=false MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
export DISPLAY="${DISPLAY:-:0}"
export XAUTHORITY="${XAUTHORITY:-$(ls -t /run/user/$(id -u)/.mutter-Xwaylandauth.* 2>/dev/null | head -1 || true)}"
u="${CAMERA_MSAA_CLI:-$PWD/target/release/unbound}"
export LD_LIBRARY_PATH="${UNBOUND_NATIVE_DIR:-$PWD/target/managed}:${LD_LIBRARY_PATH:-}"
export UNBOUND_MANAGED_DIR="${UNBOUND_MANAGED_DIR:-${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/cameramsaa/build/managed}"
out="${CAMERA_MSAA_REGRESS_OUT:-${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/cameramsaa/${1:?before or after}/regress}"
mkdir -p "$out"
heavy="$HOME/.cache/compat/with-heavy-job"
run() { local key="$1" var="$2" project="$3"; shift 3; env "$var=$out/$key" "$heavy" timeout 600 "$u" run -p "$PWD/tests/projects/$project" --no-watch "$@" > "$out/$key.log" 2>&1; }
UITK_PROBE_OUT="$out/uitk/unbound.jsonl" "$heavy" timeout 300 "$u" run -p "$PWD/tests/projects/uitk" --no-watch --frames 35 --size 960x540 > "$out/uitk.log" 2>&1
# UI Toolkit expects a file path, while the other probes expect directories.
run graphics GRAPHICS_PROBE_OUT graphics --frames 30 --size 640x480
run resources RESOURCES_PROBE_OUT resources --frames 2000 --size 640x480
run builtinrp BUILTIN_PROBE_OUT builtinrp --frames 30 --size 640x480
run builtinfx BUILTINFX_PROBE_OUT builtinfx --frames 2000 --size 640x480
run rectclip-linear RECT_CLIP_OUT uirectclipping --frames 24 --size 128x128
RECT_CLIP_MSAA=1 run rectclip-msaa RECT_CLIP_OUT uirectclipping --frames 24 --size 128x128
python3 - <<'PY'
from pathlib import Path
import shutil
for source,name in [('uirectclipping','RectGamma'),('uicolors','ColorsGamma')]:
 p=Path('tests/projects/cameramsaa/Library/Unbound')/name
 for part in ['Assets','Packages','ProjectSettings']: shutil.copytree(Path('tests/projects')/source/part,p/part,dirs_exist_ok=True)
 s=p/'ProjectSettings/ProjectSettings.asset';s.write_text(s.read_text().replace('m_ActiveColorSpace: 1','m_ActiveColorSpace: 0'))
PY
run rectclip-gamma RECT_CLIP_OUT cameramsaa/Library/Unbound/RectGamma --frames 24 --size 128x128
run colors-linear UI_COLORS_OUT uicolors --frames 8 --size 128x128
run colors-gamma UI_COLORS_OUT cameramsaa/Library/Unbound/ColorsGamma --frames 8 --size 128x128
run lifecycle CANVAS_LIFECYCLE_OUT canvaslifecycle --frames 24 --size 640x480
run transitions UITRANSITIONS_OUT uitransitions --headless 100 --fixed-fps 60

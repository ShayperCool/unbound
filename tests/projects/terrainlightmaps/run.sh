#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../../.."
export PATH="$HOME/.cargo/bin:$HOME/.dotnet:$PATH"
export DISPLAY="${DISPLAY:-:0}"
export XAUTHORITY="${XAUTHORITY:-$(ls -t /run/user/$(id -u)/.mutter-Xwaylandauth.* 2>/dev/null | head -1 || true)}"
export UseSharedCompilation=false MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
fixture="${TERRAIN_LIGHTMAP_PROJECT:-$PWD/tests/projects/terrainlightmaps}"
cache="${TERRAIN_LIGHTMAP_ROOT:-${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/terrainlightmaps}"
heavy="$HOME/.cache/compat/with-heavy-job"
managed="${UNBOUND_MANAGED_DIR:-$cache/build/managed}"
export CARGO_TARGET_DIR="${CARGO_TARGET_DIR:-$cache/build/rust}" CARGO_BUILD_JOBS=2 UNBOUND_SKIP_MANAGED_BUILD=1
cli="${TERRAIN_LIGHTMAP_CLI:-$CARGO_TARGET_DIR/release/unbound}"
export LD_LIBRARY_PATH="${UNBOUND_NATIVE_DIR:-$CARGO_TARGET_DIR/managed}:${LD_LIBRARY_PATH:-}"
editor="$(python3 - <<'PYEDITOR'
import sys
sys.path.insert(0,'tests')
from probe_paths import editor_path
print(editor_path())
PYEDITOR
)"
output="${TERRAIN_LIGHTMAP_OUT:-$cache/${2:-current}}"
export TERRAIN_LIGHTMAP_OUT="$output" UNBOUND_MANAGED_DIR="$managed"
mkdir -p "$output"
space="${3:-Linear}"
if [[ "$space" != Linear && "$space" != Gamma ]]; then echo 'color space must be Linear or Gamma' >&2; exit 2; fi
# Use a generated project view for the selected color space, leaving authored settings intact.
if [[ "${1:-}" == native || "${1:-}" == engine ]]; then
 export TERRAIN_LIGHTMAP_SOURCE="$fixture" TERRAIN_LIGHTMAP_SPACE="$space"
 fixture="$(python3 - <<'PY'
import os
from pathlib import Path
import shutil
source=Path(os.environ['TERRAIN_LIGHTMAP_SOURCE'])
space=os.environ['TERRAIN_LIGHTMAP_SPACE']
target=source/'Library/Unbound'/('Probe'+space)
for part in ('Assets','Packages','ProjectSettings'):
 shutil.copytree(source/part,target/part,dirs_exist_ok=True)
settings=target/'ProjectSettings/ProjectSettings.asset'
settings.write_text(settings.read_text().replace('m_ActiveColorSpace: 1','m_ActiveColorSpace: '+('0' if space=='Gamma' else '1')).replace('m_ActiveColorSpace: 0','m_ActiveColorSpace: '+('0' if space=='Gamma' else '1')))
print(target)
PY
)"
fi
case "${1:?command required}" in
rust-build) "$heavy" cargo build --release -p unbound -j 2 ;;
rust-test) "$heavy" cargo test --release -p unbound-shaders -j 2 ;;
create) "$heavy" "$editor" -batchmode -projectPath "$fixture" -executeMethod ProbeRunner.Create -logFile "$output/editor.log" ;;
native) "$heavy" "$editor" -batchmode -projectPath "$fixture" -executeMethod "ProbeRunner.Record$space" -logFile "$output/editor.log" ;;
build) "$heavy" dotnet publish managed/Unbound.Host/Unbound.Host.csproj -c Release -o "$managed" -p:UnboundTarget="$cache/build/obj/" --disable-build-servers -m:1 -p:UseSharedCompilation=false /nodeReuse:false ;;
engine) "$heavy" "$cli" run -p "$fixture" --no-watch --frames 40 --size 128x64 ;;
*) exit 2;;
esac

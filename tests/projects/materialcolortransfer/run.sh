#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../../.."
export PATH="$HOME/.dotnet:$HOME/.cargo/bin:$PATH" UseSharedCompilation=false MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
export DISPLAY="${DISPLAY:-:0}"
export XAUTHORITY="${XAUTHORITY:-$(ls -t /run/user/$(id -u)/.mutter-Xwaylandauth.* 2>/dev/null | head -1 || true)}"
root="${MATERIAL_COLOR_TRANSFER_ROOT:-${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/materialcolortransfer}"
fixture="${MATERIAL_COLOR_TRANSFER_PROJECT:-$PWD/tests/projects/materialcolortransfer}"
space="${MATERIAL_COLOR_TRANSFER_COLOR_SPACE:-Linear}"
if [[ "$space" != Linear && "$space" != Gamma ]]; then echo 'color space must be Linear or Gamma' >&2; exit 2; fi
export MATERIAL_COLOR_TRANSFER_OUT="${MATERIAL_COLOR_TRANSFER_OUT:-$root/${2:-current}/$space}"
# Every run but `create` (which authors the probe's assets) uses a private copy of the project:
# the probe edits loaded assets, and Unbound writes project files as the Editor does.
if [[ "$1" != create ]]; then
 export PROBE_SOURCE="$fixture" PROBE_COPY="$MATERIAL_COLOR_TRANSFER_OUT.project" PROBE_SPACE="$space"
 fixture="$(python3 - <<'PYCOPY'
import os
import sys
sys.path.insert(0, 'tests')
from probe_paths import project_copy
target = project_copy(os.environ['PROBE_SOURCE'], os.environ['PROBE_COPY'], fresh=True)
if os.environ['PROBE_SPACE'] == 'Gamma':
    settings = target/'ProjectSettings/ProjectSettings.asset'
    settings.write_text(settings.read_text().replace('m_ActiveColorSpace: 1', 'm_ActiveColorSpace: 0'))
print(target)
PYCOPY
)"
elif [[ "$space" == Gamma ]]; then
 echo 'create authors the Linear project' >&2; exit 2
fi
export UNBOUND_MANAGED_DIR="${UNBOUND_MANAGED_DIR:-$root/build/managed}"
cli="${MATERIAL_COLOR_TRANSFER_CLI:-$PWD/target/release/unbound}"
export LD_LIBRARY_PATH="${MATERIAL_COLOR_TRANSFER_NATIVE_DIR:-$PWD/target/managed}:${LD_LIBRARY_PATH:-}"
heavy="$HOME/.cache/compat/with-heavy-job"
editor="$(python3 - <<'PYEDITOR'
import sys
sys.path.insert(0,'tests')
from probe_paths import editor_path
print(editor_path())
PYEDITOR
)"
mkdir -p "$root" "$(dirname "$MATERIAL_COLOR_TRANSFER_OUT")"
# Live asset tests deliberately modify loaded assets. Preserve exact authored inputs.
saved="$fixture/Library/Unbound/MaterialColorTransferAuthored"
if [ "$1" = native ]; then
 mkdir -p "$saved"
 cp "$fixture/Assets/Resources/Imported.mat" "$saved/Imported.mat"
 cp "$fixture/Assets/Resources/Variant.mat" "$saved/Variant.mat"
fi
cleanup() {
 if [ "$1" = native ]; then
  cp "$saved/Imported.mat" "$fixture/Assets/Resources/Imported.mat"
  cp "$saved/Variant.mat" "$fixture/Assets/Resources/Variant.mat"
 fi
 MATERIAL_COLOR_TRANSFER_PROJECT="$fixture" python3 tests/projects/materialcolortransfer/cleanup.py
}
# Unity's Roslyn server can survive Editor exit. Only stop this fixture's processes.
trap 'cleanup "$1"' EXIT
case "$1" in
 create) "$heavy" timeout 600 "$editor" -batchmode -quit -force-glcore -projectPath "$fixture" -executeMethod ProbeRunner.Create -logFile "$MATERIAL_COLOR_TRANSFER_OUT.log" ;;
 native) "$heavy" timeout 600 "$editor" -batchmode -force-glcore -projectPath "$fixture" -executeMethod ProbeRunner.Record -logFile "$MATERIAL_COLOR_TRANSFER_OUT.log" ;;
 build) "$heavy" timeout 600 dotnet build managed/Unbound.Host/Unbound.Host.csproj -c Release -o "$UNBOUND_MANAGED_DIR" -p:UnboundTarget="${MATERIAL_COLOR_TRANSFER_BUILD:-$root/build/}" --disable-build-servers -m:1 -p:UseSharedCompilation=false /nodeReuse:false ;;
 engine) "$heavy" timeout 600 "$cli" run -p "$fixture" --frames 32 --size 64x64 --no-watch ;;
 *) exit 2 ;;
esac

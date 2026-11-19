#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../../.."
export PATH="$HOME/.dotnet:$HOME/.cargo/bin:$PATH" UseSharedCompilation=false MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
export DISPLAY="${DISPLAY:-:0}"
export XAUTHORITY="${XAUTHORITY:-$(ls -t /run/user/$(id -u)/.mutter-Xwaylandauth.* 2>/dev/null | head -1 || true)}"
fixture="${CAMERA_MSAA_PROJECT:-$PWD/tests/projects/cameramsaa}"
space="${CAMERA_MSAA_COLOR_SPACE:-Linear}"
if [[ "${1:-}" == gamma ]]; then space=Gamma; fi
if [[ "$space" != Linear && "$space" != Gamma ]]; then echo 'color space must be Linear or Gamma' >&2; exit 2; fi
cache="${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/cameramsaa"
profile="$space"
if [[ "${CAMERA_MSAA_FOCUSED:-0}" == 1 ]]; then profile="Focused$space"; fi
output="${CAMERA_MSAA_OUTPUT:-$cache/${2:-current}/$profile}"
# Every run but the authoring modes (create, linear) uses a private copy of the project: Unbound
# writes project files as the Editor does.
if [[ "${1:-}" != create && "${1:-}" != linear ]]; then
 export PROBE_SOURCE="$fixture" PROBE_COPY="$output.project" PROBE_SPACE="$space"
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
fi
export CAMERA_MSAA_OUT="$output" UNBOUND_MANAGED_DIR="${UNBOUND_MANAGED_DIR:-$cache/build/managed}"
cli="${CAMERA_MSAA_CLI:-$PWD/target/release/unbound}"
export LD_LIBRARY_PATH="${UNBOUND_NATIVE_DIR:-$PWD/target/managed}:${LD_LIBRARY_PATH:-}"
heavy="$HOME/.cache/compat/with-heavy-job"
editor="$(python3 - <<'PYEDITOR'
import sys
sys.path.insert(0,'tests')
from probe_paths import editor_path
print(editor_path())
PYEDITOR
)"
mkdir -p "$output"
settings="$fixture/ProjectSettings/ProjectSettings.asset"
saved_settings="$(mktemp "$output/settings.XXXXXX")"
cp "$settings" "$saved_settings"
cleanup() {
 cp "$saved_settings" "$settings"
 rm -f "$saved_settings"
 CAMERA_MSAA_PROJECT="$fixture" python3 "$PWD/tests/projects/cameramsaa/cleanup.py"
}
trap cleanup EXIT
case "${1:?create, native, gamma, linear, build or engine}" in
 create) "$heavy" timeout 600 "$editor" -batchmode -quit -projectPath "$fixture" -executeMethod ProbeRunner.Create -logFile "$output/editor.log" ;;
 native) "$heavy" timeout 600 "$editor" -batchmode -projectPath "$fixture" -executeMethod ProbeRunner.Record -logFile "$output/editor.log" ;;
 gamma) "$heavy" timeout 600 "$editor" -batchmode -projectPath "$fixture" -executeMethod ProbeRunner.RecordGamma -logFile "$output/editor.log" ;;
 linear) "$heavy" timeout 600 "$editor" -batchmode -quit -projectPath "$fixture" -executeMethod ProbeRunner.SetLinear -logFile "$output/editor.log" ;;
 build) "$heavy" timeout 600 dotnet build managed/Unbound.Host/Unbound.Host.csproj -c Release -o "$UNBOUND_MANAGED_DIR" -p:UnboundTarget="${CAMERA_MSAA_BUILD:-$cache/build/obj/}" --disable-build-servers -m:1 -p:UseSharedCompilation=false /nodeReuse:false ;;
 engine) "$heavy" timeout 600 "$cli" run -p "$fixture" --frames 64 --size 128x128 --no-watch ;;
 *) exit 2 ;;
esac

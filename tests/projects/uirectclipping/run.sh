#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../../.."
export PATH="$HOME/.cargo/bin:$HOME/.dotnet:$PATH"
export UseSharedCompilation=false MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
export LD_LIBRARY_PATH="/mnt/storage-hdd/node-b-caches-20261023/unialt-target-compat/managed:${LD_LIBRARY_PATH:-}"
export DISPLAY=:0
export XAUTHORITY="$(ls -t /run/user/1000/.mutter-Xwaylandauth.* | head -1)"
fixture="$PWD/tests/projects/uirectclipping"
output="$HOME/.cache/compat/rectclip-${2:-run}"
managed="${UNBOUND_MANAGED_DIR:-$HOME/.cache/compat/rectclip-managed}"
heavy="$HOME/.cache/compat/with-heavy-job"
# Runs (not the authoring modes) use a private copy of the project (tests/probe_paths.py
# project_copy): Unbound and the Editor write project files.
case "${1:-}" in
 native|native-gamma|engine) fixture="$(python3 -c 'import sys; sys.path.insert(0, "tests"); from probe_paths import project_copy; print(project_copy(sys.argv[1], sys.argv[2], fresh=True))' "$fixture" "$output.project")" ;;
esac
trap 'RECT_CLIP_PROJECT="$fixture" python3 tests/projects/uirectclipping/cleanup.py' EXIT
editor="$HOME/Unity/Hub/Editor/6000.0.71f1/Editor/Unity"
case "${1:-}" in
 create) "$heavy" timeout 600 "$editor" -batchmode -quit -projectPath "$fixture" -executeMethod ProbeRunner.Create -logFile "$output.log" ;;
 set-linear) "$heavy" timeout 600 "$editor" -batchmode -quit -projectPath "$fixture" -executeMethod ProbeRunner.SetLinear -logFile "$output.log" ;;
 native-gamma) RECT_CLIP_OUT="$output" "$heavy" timeout 600 "$editor" -batchmode -projectPath "$fixture" -executeMethod ProbeRunner.RecordGamma -logFile "$output.log" ;;
 native) RECT_CLIP_OUT="$output" "$heavy" timeout 600 "$editor" -batchmode -projectPath "$fixture" -executeMethod ProbeRunner.Record -logFile "$output.log" ;;
 build) "$heavy" timeout 600 dotnet build managed/Unbound.Host/Unbound.Host.csproj -c Release -o "$managed" -p:UnboundTarget="$HOME/.cache/compat/rectclip-build/" --disable-build-servers -m:1 -p:UseSharedCompilation=false /nodeReuse:false ;;
 engine) RECT_CLIP_OUT="$output" UNBOUND_MANAGED_DIR="$managed" "$heavy" timeout 600 /mnt/storage-hdd/node-b-caches-20261023/unialt-target-compat/release/unbound run -p "$fixture" --frames 24 --size 128x128 --no-watch ;;
 *) echo 'usage: run.sh {create|native|native-gamma|set-linear|build|engine} [evidence-name]' >&2; exit 2 ;;
esac

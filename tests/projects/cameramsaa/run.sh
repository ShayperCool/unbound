#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../../.."
export PATH="$HOME/.dotnet:$HOME/.cargo/bin:$PATH" UseSharedCompilation=false MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
export DISPLAY=:0 XAUTHORITY="$(ls -t /run/user/1000/.mutter-Xwaylandauth.* | head -1)"
export LD_LIBRARY_PATH="/mnt/storage-hdd/node-b-caches-20261023/unialt-target-compat/managed:${LD_LIBRARY_PATH:-}"
fixture="$PWD/tests/projects/cameramsaa"
output="$HOME/.cache/compat/cameramsaa-${2:-run}"
export CAMERA_MSAA_OUT="$output" UNBOUND_MANAGED_DIR="${UNBOUND_MANAGED_DIR:-$HOME/.cache/compat/cameramsaa-managed}"
heavy="$HOME/.cache/compat/with-heavy-job"
editor="$HOME/Unity/Hub/Editor/6000.0.71f1/Editor/Unity"
trap 'python3 "$fixture/cleanup.py"' EXIT
case "$1" in
 create) "$heavy" timeout 600 "$editor" -batchmode -quit -projectPath "$fixture" -executeMethod ProbeRunner.Create -logFile "$output.log" ;;
 native) "$heavy" timeout 600 "$editor" -batchmode -projectPath "$fixture" -executeMethod ProbeRunner.Record -logFile "$output.log" ;;
 gamma) "$heavy" timeout 600 "$editor" -batchmode -projectPath "$fixture" -executeMethod ProbeRunner.RecordGamma -logFile "$output.log" ;;
 linear) "$heavy" timeout 600 "$editor" -batchmode -quit -projectPath "$fixture" -executeMethod ProbeRunner.SetLinear -logFile "$output.log" ;;
 build) "$heavy" timeout 600 dotnet build managed/Unbound.Host/Unbound.Host.csproj -c Release -o "$UNBOUND_MANAGED_DIR" -p:UnboundTarget="$HOME/.cache/compat/cameramsaa-build/" --disable-build-servers -m:1 -p:UseSharedCompilation=false /nodeReuse:false ;;
 engine) "$heavy" timeout 600 /mnt/storage-hdd/node-b-caches-20261023/unialt-target-compat/release/unbound run -p "$fixture" --frames 24 --size 128x128 --no-watch ;;
 *) exit 2 ;;
esac

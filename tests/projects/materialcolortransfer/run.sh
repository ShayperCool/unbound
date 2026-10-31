#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../../.."
export PATH="$HOME/.dotnet:$HOME/.cargo/bin:$PATH" UseSharedCompilation=false MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
export DISPLAY=:0 XAUTHORITY="$(ls -t /run/user/1000/.mutter-Xwaylandauth.* | head -1)"
root="${MATERIAL_COLOR_TRANSFER_ROOT:-$HOME/.cache/compat/materialcolortransfer-6239018d}"
fixture="${MATERIAL_COLOR_TRANSFER_PROJECT:-$PWD/tests/projects/materialcolortransfer}"
export MATERIAL_COLOR_TRANSFER_OUT="${MATERIAL_COLOR_TRANSFER_OUT:-$root/${1:?command}-${2:-run}}"
export UNBOUND_MANAGED_DIR="${UNBOUND_MANAGED_DIR:-$root/before-managed}"
export LD_LIBRARY_PATH="${MATERIAL_COLOR_TRANSFER_NATIVE_DIR:-$HOME/.cache/compat/parity-baseline-native6}:${LD_LIBRARY_PATH:-}"
cli="${MATERIAL_COLOR_TRANSFER_CLI:-$HOME/.cache/compat/parity-baseline-native6/unbound}"
heavy="$HOME/.cache/compat/with-heavy-job"
editor="$HOME/Unity/Hub/Editor/6000.0.71f1/Editor/Unity"
mkdir -p "$root" "$(dirname "$MATERIAL_COLOR_TRANSFER_OUT")"
# Live asset tests deliberately modify loaded assets. Preserve exact authored inputs.
saved="$fixture/Library/MaterialColorTransferAuthored"
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

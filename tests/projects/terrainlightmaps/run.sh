#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../../.."
export PATH="$HOME/.cargo/bin:$HOME/.dotnet:$PATH"
export DISPLAY=:0
export XAUTHORITY="$(ls -t /run/user/1000/.mutter-Xwaylandauth.* | head -1)"
export LD_LIBRARY_PATH="$PWD/target/terrain-lightmaps/rust/managed:/mnt/storage-hdd/node-b-caches-20261023/unialt-target-compat/managed:${LD_LIBRARY_PATH:-}"
export UseSharedCompilation=false MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
fixture="$PWD/tests/projects/terrainlightmaps"
heavy="$HOME/.cache/compat/with-heavy-job"
managed="$PWD/target/terrain-lightmaps/managed"
export CARGO_TARGET_DIR="$PWD/target/terrain-lightmaps/rust" CARGO_BUILD_JOBS=2 UNBOUND_SKIP_MANAGED_BUILD=1
case "$1" in
rust-build) "$heavy" cargo build --release -p unbound -j 2 ;;
rust-test) "$heavy" cargo test --release -p unbound-shaders -j 2 ;;
create) "$heavy" "$HOME/Unity/Hub/Editor/6000.0.71f1/Editor/Unity" -batchmode -projectPath "$fixture" -executeMethod ProbeRunner.Create -logFile "$fixture/Library/create.log" ;;
native) export TERRAIN_LIGHTMAP_OUT="$fixture/Reference/$2"; "$heavy" "$HOME/Unity/Hub/Editor/6000.0.71f1/Editor/Unity" -batchmode -projectPath "$fixture" -executeMethod "ProbeRunner.Record$3" -logFile "$fixture/Library/$2.log" ;;
build) "$heavy" dotnet publish managed/Unbound.Host/Unbound.Host.csproj -c Release -o "$managed" -p:UnboundTarget="$PWD/target/terrain-lightmaps/build/" --disable-build-servers -m:1 -p:UseSharedCompilation=false /nodeReuse:false ;;
engine) export UNBOUND_MANAGED_DIR="$managed" TERRAIN_LIGHTMAP_OUT="$fixture/Library/$2"; "$heavy" "$CARGO_TARGET_DIR/release/unbound" run -p "$fixture" --no-watch --frames 40 --size 128x64 ;;
*) exit 2;;
esac

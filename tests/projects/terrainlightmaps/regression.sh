#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../../.."
export PATH="$HOME/.cargo/bin:$HOME/.dotnet:$PATH" DISPLAY=:0
export XAUTHORITY="$(ls -t /run/user/1000/.mutter-Xwaylandauth.* | head -1)"
export UseSharedCompilation=false MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
export LD_LIBRARY_PATH="$PWD/target/terrain-lightmaps/rust/managed:/mnt/storage-hdd/node-b-caches-20261023/unialt-target-compat/managed:${LD_LIBRARY_PATH:-}"
export UNBOUND_MANAGED_DIR="${TERRAIN_REG_MANAGED:-$PWD/target/terrain-lightmaps/managed}"
export TERRAIN_PROBE_OUT="${TERRAIN_REG_OUT:-$PWD/tests/projects/terrainlightmaps/Library/terrain-regression}"
"$HOME/.cache/compat/with-heavy-job" "$PWD/target/terrain-lightmaps/rust/release/unbound" run -p tests/projects/terrain --frames 300 --size 320x180 --no-watch

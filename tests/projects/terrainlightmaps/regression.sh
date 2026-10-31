#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../../.."
export PATH="$HOME/.cargo/bin:$HOME/.dotnet:$PATH" DISPLAY="${DISPLAY:-:0}"
export XAUTHORITY="${XAUTHORITY:-$(ls -t /run/user/$(id -u)/.mutter-Xwaylandauth.* 2>/dev/null | head -1 || true)}"
export UseSharedCompilation=false MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
cache="${TERRAIN_LIGHTMAP_ROOT:-${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/terrainlightmaps}"
cli="${TERRAIN_LIGHTMAP_CLI:-$cache/build/rust/release/unbound}"
export LD_LIBRARY_PATH="${UNBOUND_NATIVE_DIR:-$cache/build/rust/managed}:${LD_LIBRARY_PATH:-}"
export UNBOUND_MANAGED_DIR="${TERRAIN_REG_MANAGED:-$cache/build/managed}"
export TERRAIN_PROBE_OUT="${TERRAIN_REG_OUT:-$cache/current/terrain-regression}"
mkdir -p "$TERRAIN_PROBE_OUT"
"$HOME/.cache/compat/with-heavy-job" "$cli" run -p tests/projects/terrain --frames 300 --size 320x180 --no-watch

#!/usr/bin/env bash
# Run from the repository root. Historical drivers write only into these private paths.
set -euo pipefail
source /mnt/storage-hdd/node-b-caches-20261023/query2d-continuation-t3-06ecf6ba/env.sh
export UNBOUND_MANAGED_DIR="$QUERY_WORK/managed-final"
export UnboundNativeDir="$UNBOUND_MANAGED_DIR" LD_LIBRARY_PATH="$UNBOUND_MANAGED_DIR"
# Prepare managed-final as a private writable copy before invoking this script.
~/.cache/compat/with-heavy-job dotnet build managed/Unbound.Host/Unbound.Host.csproj \
  -c Release -o "$UNBOUND_MANAGED_DIR" -p:UnboundTarget="$UnboundTarget" \
  -p:UnboundNativeDir="$UnboundNativeDir" --disable-build-servers -m:1 \
  -p:UseSharedCompilation=false /nodeReuse:false > "$QUERY_WORK/validation/build-final.log" 2>&1
python3 tests/projects/query2dcontinuation/capture_existing.py --label final
python3 tests/projects/query2dcontinuation/validate.py > "$QUERY_WORK/validation/gates.log" 2>&1
python3 tests/projects/query2dcontinuation/check_gates.py > "$QUERY_WORK/validation/check-gates.log" 2>&1
python3 tests/projects/query2dcontinuation/report.py > "$QUERY_WORK/validation/report.log" 2>&1

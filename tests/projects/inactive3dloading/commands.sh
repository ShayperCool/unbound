#!/usr/bin/env bash
# Exact final commands. Use a fresh private LOADING_WORK layout for a new replay.
set -euo pipefail
: "${LOADING_WORK:?source the private env.sh first}"
~/.cache/compat/with-heavy-job dotnet build managed/Unbound.Host/Unbound.Host.csproj \
  -c Release -o "$UNBOUND_MANAGED_DIR" -p:UnboundTarget="$UnboundTarget" \
  -p:UnboundNativeDir="$UnboundNativeDir" --disable-build-servers -m:1 \
  -p:UseSharedCompilation=false /nodeReuse:false > "$LOADING_WORK/logs/build-final.log" 2>&1
~/.cache/compat/with-heavy-job dotnet test managed/UnityEngine.Tests/UnityEngine.Tests.csproj \
  -c Release -p:UnboundTarget="$LOADING_WORK/test-build/" -p:UnboundNativeDir="$UnboundNativeDir" \
  --disable-build-servers -m:1 -p:UseSharedCompilation=false /nodeReuse:false \
  --filter 'FullyQualifiedName~UnityEngine.Tests.PhysicsTests|FullyQualifiedName~UnityEngine.Tests.Physics2DTests|FullyQualifiedName~UnityEngine.Tests.UIElements|FullyQualifiedName~UnityEngine.Tests.YogaLayout|FullyQualifiedName~UnityEngine.Tests.CanvasOrderTests|FullyQualifiedName~UnityEngine.Tests.CoreTests' \
  --logger 'trx;LogFileName=final.trx' --results-directory "$LOADING_WORK/tests" \
  > "$LOADING_WORK/logs/tests-final.log" 2>&1
for order in transform-first rigidbody-first; do
  python3 tests/projects/inactive3dloading/run.py prepare --order "$order" --label final
  python3 tests/projects/inactive3dloading/run.py engine --order "$order" --label final
done
python3 tests/projects/inactive3dloading/report.py --label final
bash tests/projects/inactive3dloading/validate-stage.sh > "$LOADING_WORK/logs/regressions-final.log" 2>&1
python3 tests/projects/inactive3dloading/verify.py --full
python3 tests/projects/inactive3dloading/archive.py

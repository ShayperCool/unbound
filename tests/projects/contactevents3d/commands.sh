#!/usr/bin/env bash
# Reproduction sequence. Set CONTACT_WORK to a fresh private directory first.
set -euo pipefail
: "${CONTACT_WORK:?set a fresh private output directory}"
export PATH="$HOME/.cargo/bin:$HOME/.dotnet:$PATH"
unset WT
export CARGO_TARGET_DIR="$CONTACT_WORK/cargo"
export UNBOUND_MANAGED_DIR="$CONTACT_WORK/managed" UnboundTarget="$CONTACT_WORK/build/"
export UnboundNativeDir="$CONTACT_WORK/native" LD_LIBRARY_PATH="$CONTACT_WORK/native"
export SCENE_SETTINGS_CLI="$CONTACT_WORK/unbound"
export UNBOUND_CACHE="$CONTACT_WORK/cache" XDG_CACHE_HOME="$CONTACT_WORK/xdg-cache"
export NUGET_PACKAGES="$CONTACT_WORK/nuget" DOTNET_CLI_HOME="$CONTACT_WORK/dotnet-home"
export UNBOUND_UNITY="$HOME/Unity/Hub/Editor/6000.0.71f1/Editor"
export UseSharedCompilation=false MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
mkdir -p "$CONTACT_WORK/logs" "$NUGET_PACKAGES"
cp -a --reflink=auto "$HOME/.nuget/packages/." "$NUGET_PACKAGES/"
cp --reflink=auto "$HOME/.cache/compat/parity-baseline-loading3d-native8/unbound" "$SCENE_SETTINGS_CLI"
python3 tests/projects/contactevents3d/run.py create --label created
for profile in 0 1 2 3; do
    python3 tests/projects/contactevents3d/run.py native --label named1 --profile "$profile"
    python3 tests/projects/contactevents3d/run.py native --label named2 --profile "$profile"
    UNBOUND_MANAGED_DIR="$HOME/.cache/compat/parity-baseline-loading3d-native8/managed" \
    LD_LIBRARY_PATH="$HOME/.cache/compat/parity-baseline-loading3d-native8" \
    SCENE_SETTINGS_CLI="$HOME/.cache/compat/parity-baseline-loading3d-native8/unbound" \
        python3 tests/projects/contactevents3d/run.py engine --label named-baseline --profile "$profile"
done
for repeat in 1 2; do
    python3 tests/projects/contactevents3d/controls.py native --label "order$repeat"
    python3 tests/projects/contactevents3d/geometry.py native --label "mesh$repeat"
    python3 tests/projects/contactevents3d/layout.py native --label "native$repeat"
done
~/.cache/compat/with-heavy-job python3 tests/projects/physicsscenesettings/build_sdk_probe.py \
    --physx /mnt/storage-hdd/node-b-caches-20261023/sdk-physx-parity-20261031/sdk8-callback/libunbound_physx_sdk.a \
    --nvcloth /mnt/storage-hdd/node-b-caches-20261023/unialt-target-compat/release/build/unbound-host-fc475d437adac648/out/nvcloth-99cdb160346e36f5/libunbound_nvcloth_sdk.a \
    --output "$UnboundNativeDir" --shim "$PWD/native/unbound_physx.cpp" > "$CONTACT_WORK/logs/native.log" 2>&1
~/.cache/compat/with-heavy-job dotnet build managed/Unbound.Host/Unbound.Host.csproj -c Release \
    -o "$UNBOUND_MANAGED_DIR" -p:UnboundTarget="$UnboundTarget" -p:UnboundNativeDir="$UnboundNativeDir" \
    --disable-build-servers -m:1 -p:UseSharedCompilation=false /nodeReuse:false > "$CONTACT_WORK/logs/build.log" 2>&1
cp --reflink=auto "$UnboundNativeDir/libunbound_physx.so" "$UNBOUND_MANAGED_DIR/libunbound_physx.so"
cp --reflink=auto "$UnboundNativeDir/libunbound_physx.so" "$CONTACT_WORK/libunbound_physx.so"
~/.cache/compat/with-heavy-job dotnet test managed/UnityEngine.Tests/UnityEngine.Tests.csproj -c Release \
    -p:UnboundTarget="$CONTACT_WORK/test-build/" -p:UnboundNativeDir="$UnboundNativeDir" \
    --disable-build-servers -m:1 -p:UseSharedCompilation=false /nodeReuse:false \
    --filter 'FullyQualifiedName~UnityEngine.Tests.PhysicsTests|FullyQualifiedName~UnityEngine.Tests.Physics2DTests|FullyQualifiedName~UnityEngine.Tests.UIElements|FullyQualifiedName~UnityEngine.Tests.YogaLayout|FullyQualifiedName~UnityEngine.Tests.CanvasOrderTests|FullyQualifiedName~UnityEngine.Tests.CoreTests|FullyQualifiedName~UnityEngine.Tests.PhysicsContactEventTests' \
    --logger 'trx;LogFileName=accepted.trx' --results-directory "$CONTACT_WORK/tests" > "$CONTACT_WORK/logs/tests.log" 2>&1
for profile in 0 1 2 3; do
    python3 tests/projects/contactevents3d/run.py engine --label named --profile "$profile"
done
python3 tests/projects/contactevents3d/controls.py engine --label accepted
python3 tests/projects/contactevents3d/geometry.py engine --label accepted
export LOADING_WORK="$CONTACT_WORK" TRIGGER_WORK="$CONTACT_WORK/validation/engine"
export SCENE_WORK="$TRIGGER_WORK/work" SCENE_PARENT_WORK="$TRIGGER_WORK/probes"
mkdir -p "$CONTACT_WORK/validation"
python3 tests/projects/contactevents3d/validate.py > "$CONTACT_WORK/logs/regressions-accepted.log" 2>&1
# verify_regressions.py and archive.py also archive this run's exact logs and manifests.
# Their original run used build-accepted.log/tests-accepted.log; retain those names.
cp "$CONTACT_WORK/logs/build.log" "$CONTACT_WORK/logs/build-accepted.log"
cp "$CONTACT_WORK/logs/tests.log" "$CONTACT_WORK/logs/tests-accepted.log"
python3 tests/projects/contactevents3d/verify_regressions.py

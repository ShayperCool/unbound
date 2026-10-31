#!/usr/bin/env bash
# Reproduction outline. Adapt Evidence/env.sh to a NEW private work directory first.
# Historical captures and reports are append-only; never rerun labels in an old work directory.
set -euo pipefail
source tests/projects/physics2dmotion/Evidence/env.sh
cp -a --reflink=auto ~/.cache/compat/parity-baseline-lifecycle2d-native8/managed "$MOTION_WORK/managed-baseline"
cp -a --reflink=auto ~/.cache/compat/parity-baseline-lifecycle2d-native8/managed "$MOTION_WORK/managed-final"
chmod -R u+w "$MOTION_WORK/managed-final"

# Source version1 was committed at de09ee57. Sources/ProbeEntry-v1.cs.txt retains it.
# For version1 preparation set MOTION_SOURCE=ProbeEntry-v1.cs.txt and choose fresh labels.
# The current Source is version2, running the unchanged complete original sequence.
for label in native1 native2; do
    python3 tests/projects/physics2dmotion/run.py prepare --label "$label"
    python3 tests/projects/physics2dmotion/run.py native --label "$label"
done
for label in rotation-native1 rotation-native2; do
    MOTION_SOURCE=MoveRotation.cs python3 tests/projects/physics2dmotion/run.py prepare --label "$label"
    python3 tests/projects/physics2dmotion/run.py native --label "$label"
done
# Native agreement must be checked before applying a new behavior. Existing archives
# include every exact input hash, source version, Native log and both full raw captures.
# Original baseline uses MOTION_MANAGED=managed-baseline and the frozen matching CLI.
MOTION_PROJECT=project-native1 MOTION_MANAGED=managed-baseline \
    python3 tests/projects/physics2dmotion/run.py engine --label broad-parent

~/.cache/compat/with-heavy-job dotnet build managed/Unbound.Host/Unbound.Host.csproj \
    -c Release -o "$UNBOUND_MANAGED_DIR" -p:UnboundTarget="$UnboundTarget" \
    -p:UnboundNativeDir="$UnboundNativeDir" --disable-build-servers -m:1 \
    -p:UseSharedCompilation=false /nodeReuse:false > "$MOTION_WORK/build-final.log" 2>&1
MOTION_PROJECT=project-native1 python3 tests/projects/physics2dmotion/run.py engine --label final
python3 tests/projects/physics2dmotion/skin.py baseline --baseline
python3 tests/projects/physics2dmotion/skin.py final
for probe in lifecycle receivers timing order; do
    python3 tests/projects/trigger2dlifecycle/run.py prepare --probe "$probe"
    python3 tests/projects/trigger2dlifecycle/run.py engine --probe "$probe" --label final
done
for group in focused broad matrix visual; do
    python3 tests/projects/trigger2dlifecycle/validate.py "$group"
done
export LOADING_WORK="$MOTION_WORK/loading"
mkdir -p "$LOADING_WORK/created" "$LOADING_WORK/logs"
for folder in Assets Packages ProjectSettings; do
    cp -a --reflink=auto "tests/projects/inactive3dloading/$folder" "$LOADING_WORK/created/$folder"
done
for order in transform-first rigidbody-first; do
    python3 tests/projects/inactive3dloading/run.py prepare --order "$order" --label final
done
# validate.py runs both loading orders and the exact six-class managed filter plus
# Physics2DMotionParityTests, with the same build-server/parallelism flags.
python3 tests/projects/physics2dmotion/validate.py
python3 tests/projects/physics2dmotion/report.py final --native native1 --baseline broad-parent --archive final-replay
for fixture in physicsscenesettings joints2d effectors; do
    python3 -m unittest discover -s "tests/projects/$fixture" -p test_compare.py
done
python3 tests/projects/physics2dmotion/verify.py --archive validation-replay --motion-report final-replay

# The rejected candidate sequence used the same build command, fresh output labels
# candidate1..candidate4 and their preserved Evidence/<label>/source.patch against
# the parent. Version1 supplies candidate1/2; version2 supplies candidate3/4.
# Each build was followed by run.py engine, then report.py with the corresponding
# Native/baseline pair, and candidate3/4 also ran the original broad/joint/effector/force gates.
# Rotation1 used Evidence/rotation1-source.patch; its two independent Native recordings
# preceded that patch. rotation-candidate1-complete is the complete direct-control report,
# rotation-motion1 the complete full-sequence report. Neither patch is production code.

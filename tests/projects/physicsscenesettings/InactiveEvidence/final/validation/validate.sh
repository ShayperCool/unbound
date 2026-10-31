#!/usr/bin/env bash
set -euo pipefail
source /mnt/storage-hdd/node-b-caches-20261023/inactive3d-t3-3a3af6f7-20261031/env.sh
runner=tests/projects/physicsscenesettings
for probe in sleep-inactive inactive-sequences sleep triggers flags trigger-pairs ignore-lifecycle kinematic order component barrier; do
 python3 "$runner/parent_probes.py" prepare --probe "$probe"
 python3 "$runner/parent_probes.py" engine --probe "$probe" --label final
done
python3 "$runner/run.py" prepare
python3 "$runner/run.py" final
python3 "$runner/contactpairs.py" final
python3 "$runner/contactpairs.py" final --noise
python3 "$runner/regressions.py" final
rm -f "$INACTIVE_WORK/graphics.json"
GRAPHICS_PROBE_OUT="$INACTIVE_WORK/graphics.json" ~/.cache/compat/with-heavy-job timeout 240 "$SCENE_SETTINGS_CLI" run -p tests/projects/graphics --no-watch --frames 30 > "$INACTIVE_WORK/graphics.log" 2>&1
python3 tests/projects/graphics/compare.py tests/projects/graphics/Reference/unity-6000.0.71f1.json "$INACTIVE_WORK/graphics.json" > "$INACTIVE_WORK/graphics-comparison.log"

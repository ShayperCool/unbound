# NavMeshObstacle probe

`ObstacleProbe` plays NavMesh obstacles and agents at a fixed 0.02 s step for 250 frames, one lane
per flat 16 m cell (cells 30 m apart; the level and its baked NavMesh are committed in
`Assets/Scenes`). An agent usually walks from x = -6 to x = 6 through the lane's obstacle.

| lanes | what they vary |
| --- | --- |
| `carve_box`, `carve_rot`, `carve_capsule`, `carve_offset`, `carve_block` | carving shapes: box, rotated box, capsule, offset centre, a wall across the cell |
| `carve_late`, `carve_enable`, `carve_walkinto`, `carve_on_agent` | carving switched on, enabled or created later; under a standing agent |
| `carve_stationary_move`, `carve_always_move`, `carve_threshold`, `carve_time` | moving carving obstacles: carveOnlyStationary, carvingMoveThreshold, carvingTimeToStationary |
| `avoid_box`, `avoid_capsule`, `avoid_rot`, `avoid_side`, `avoid_long`, `avoid_moving`, `avoid_velocity`, `avoid_radius1`, `avoid_lowq` | obstacles that do not carve, as avoidance obstacles |
| `defaults` | an obstacle as added |

Recorded per frame: the agent ("a": position, next position, velocity, desired velocity,
steering target, remaining distance, path flags, corners), the obstacle ("o": position,
velocity), NavMesh queries from Update ("u", after the lane's scripted action) and LateUpdate
("l") — SamplePosition at the cell's centre, CalculatePath and Raycast from x = -6 to x = 6 — and
at some frames the triangulation around the cell's centre ("t": sorted vertices, triangle count).

    python3 tests/projects/navobstacle/run.py setup   --release 6000.0.71f1 --label L   # level + NavMesh
    python3 tests/projects/navobstacle/run.py editor  --release 6000.0.71f1 --label L   # two Editor captures
    python3 tests/projects/navobstacle/run.py publish --release 6000.0.71f1 --label L   # identical? -> Reference/
    UNBOUND_CLI=.../unbound python3 tests/projects/navobstacle/run.py unbound --release 6000.0.71f1 --label L
    python3 tests/projects/navobstacle/compare.py tests/projects/navobstacle/Reference/unity-6000.0.71f1.jsonl \
        ~/.cache/unbound/validation/runs/navobstacle/L/unbound/values.jsonl --report /path/report.jsonl

`compare.py` holds positions, corners and distances to 1e-3 and velocities to 2e-3, everything
else exactly, and counts every missing, extra or failing value. Runs work on fresh copies of the
project under `$XDG_CACHE_HOME/unbound/validation/runs/navobstacle/<label>` (labels are never
reused); Editors run through the shared heavy-job launcher on the GPU and exit by themselves.

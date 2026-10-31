# Query continuation

The starting diagnostic commit is 29c7291dde2a85872617ff4bdfb551fe8a67eca5.
The accepted branch remains at 88a53c477dc43e646c21522f6c402c0cec98c6c1.
This work does not integrate the other owners' fixes or establish acceptance.

Two fresh Unity 6000.0.71f1 Linux CPU processes agree byte for byte on all
6,256 observations in 336 initial-overlap controls. They vary four target shapes,
three query shapes, fourteen gaps, both queriesStartInColliders values, zero and
short ranges, and casts toward and away from the target. The Native recordings
precede implementation changes. The new controls use the historical broad
fixture's unmodified ProbeRunner and ProbeLog in private copies.

Native distinguishes the initial query TOI threshold from surface overlap.
For example, a circle penetrating an unrounded box by 0.001 travels about 0.004
before reporting a hit, regardless of queriesStartInColliders. A penetration of
0.004 instead produces an initial hit when that flag is true and no hit when
false. Short ranges expose the existing root tolerance, including midpoint hits.
A zero range and a direction away from a shallow overlap both miss.

All generated projects, builds and caches are private:
`/mnt/storage-hdd/node-b-caches-20261023/query2d-continuation-t3-06ecf6ba/`.
Use its env.sh, which sources the shared PATH/DISPLAY setup and unsets WT.
Every Unity, dotnet and engine process takes the global heavy-job lock.

```bash
source /mnt/storage-hdd/node-b-caches-20261023/query2d-continuation-t3-06ecf6ba/env.sh
python3 tests/projects/query2dcontinuation/run.py prepare --label NEW_LABEL
python3 tests/projects/query2dcontinuation/run.py native --label NEW_LABEL
```

Labels and project copies are write-once. A second fresh label is required for
independent Native agreement. Existing query2dparity and colliderskin2d fixtures,
recordings and checkers remain unchanged. Older Unity releases are unmeasured.

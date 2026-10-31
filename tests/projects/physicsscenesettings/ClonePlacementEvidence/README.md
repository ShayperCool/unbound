# Clone placement continuation

The parent added two controls before accepting the inactive-body implementation.
`ParentProbes/InactiveClonePlacement.cs` records 160 observations across active
and inactive sources, dynamic and kinematic bodies, translated/rotated source
parents, explicit poses and both target-parent modes. Source velocities are
nonzero for dynamic bodies. `TransformClonePlacement.cs` adds 12 observations
without physics, including both null-parent flags. Each suite has two independent,
byte-identical Unity 6000.0.71f1 Linux recordings committed here.

The pre-inactive baseline passes 88/160. The inactive-only candidate passes
124/160, resolving 46 keys while introducing ten and retaining 26. `before.json`
and both complete engine outputs preserve every value. That state was rejected
before integration into the continuation branch. Default no-parent placement
also fails six of the 12 Transform-only controls in the old implementation.

Native snapshots the initial inactive body pose after clone placement. Cloned
linear and angular motion begin at zero. No-parent GameObject clones keep copied
local state unless explicit coordinates are supplied, including either flag
with a null parent. Non-null parent modes preserve the requested local/world
placement. Both physics and ordinary Transform controls support these results.

The first correction passes all 172 placement observations but overwrites copied
RectTransform anchors. `rect-before/` preserves the complete 8671-record Canvas
capture and all 27 differences. The final correction leaves copied root fields
intact when no explicit pose is requested. Final parent controls pass 172/172
exactly and Canvas8671/8671, without changing any reference or tolerance.

The same final runtime passes inactive1050, sequences4668, active20700, matrix5055,
tests353, graphics80, messages316 and API0/2266. Complete accepted results, artifact
hashes, source hashes and commands are under
`../InactiveEvidence/ParentIntegration/`. The main ABI5 and shim8 are unchanged.

Private recording/build artifacts are under
`/mnt/storage-hdd/node-b-caches-20261023/inactive3d-integration-20261031/`.
Use parent_probes.py with `clone-placement` and `transform-clone-placement`;
Native recordings require repeats1 and2 before runtime changes. Engine captures
use the matching private managed output and frozen ABI5 CLI, with every heavy
process under `~/.cache/compat/with-heavy-job`. Older releases, further hierarchy
and scale cases and full engine parity remain unmeasured.

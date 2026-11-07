# Scheduling, terrain and controller filter controls

The additional controls use Unity 6000.0.71f1 on the Linux GPU Editor. The canonical
TSV preserves every earlier reference value. Independent private projects record
the same complete output before publication. Receipts, repeat captures, logs and
comparison reports stay in the validation cache.

## Contracts consulted

The versioned 6000.0 documentation distinguishes these guarantees from the measured
implementation details below:

- [ScheduleBatch](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RaycastCommand.ScheduleBatch.html)
  schedules a job, waits for its dependency before querying, and uses
  `minCommandsPerJob` as the minimum work granularity.
- [RaycastCommand](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RaycastCommand.html)
  requires completion before reading results. Each command owns `maxHits` slots;
  the first null collider terminates a block. Later unused slots have no promised
  value. Multiple mesh faces apply to rays, while other shapes return at most one
  hit per collider. No hit order is promised.
- [TerrainCollider](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/TerrainCollider.html)
  represents the heightmap and inherits collider material properties.
  [sharedMaterial](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Collider-sharedMaterial.html)
  shares property edits across colliders using that material. These pages do not
  specify the lifetime of a terrain tree's material binding.
- [CharacterController](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/CharacterController.html)
  moves through `Move`, constrained by collisions. The
  [component manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-CharacterController.html)
  warns that inspector property edits recreate the controller and lose trigger
  contacts until it moves again; its query capsule can also be slightly smaller.
  Neither page specifies collision-cache invalidation after filter edits.
- [IgnoreLayerCollision](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics.IgnoreLayerCollision.html)
  ignores the named layers and may reset affected trigger state.
  [IgnoreCollision](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics.IgnoreCollision.html)
  applies to a collider pair; that state is not persisted in a saved scene.

## Causes and measured behavior

| Mechanism | Observation and implementation |
| --- | --- |
| Buffer ownership | Reading pending results, writing pending commands, disposing a result alias, or scheduling a competing writer throws `InvalidOperationException`. Reading commands is allowed unless a dependent command writer still owns them. Invalid capacity returns without completing the supplied dependency. All eight command families retain result ownership until completion. The existing job scheduler supplies container validation, dependency tracking and safety release. |
| Partitioning | Values -1, 0, 1, 3, 64 and int.MaxValue complete an uneven 17-command batch with unchanged results. Nonpositive values use a minimum chunk of one. Parallel jobs divide command indices into chunks and write disjoint `maxHits` blocks. Tests also cover 1,031 commands with granularity 7 and 2,048 and int.MaxValue, pending writers and dependent readers. Exact worker counts and execution order are not promised. |
| Scene access | Scheduling prepares worlds on the calling thread. Query jobs read those worlds using thread-local scratch. Native scene mutations wait for outstanding query execution, without synchronizing the user's array handles. Closest-point jobs snapshot collider poses and supported geometry before execution, including disabled colliders. |
| Terrain queries | Rays, sphere/box/capsule sweeps and sphere/box/capsule overlaps cover both a flat heightmap and a tree capsule, with one and four result slots. Each batch is recorded beside the corresponding single-call query. |
| Tree materials | Trees bind the terrain collider's material when their shapes are created. Assigning `sharedMaterial` or `material`, including first reading an instance material, updates the heightmap but retains the trees' original binding. Editing the original material's restitution changes both shapes that still use it. Controls include high/low initial values, assignments in both directions and mutations in both directions. `Compat.TerrainTreeMaterialRetainsCreationBinding` selects the measured binding lifetime. Other releases retain assignment propagation until measured. |
| Controller filters | Two warmed controllers undergo eight successive changes without teleporting. Matrix changes affect both; a pair or object-layer change affects the named controller/wall. Unrelated layer pairs and repeated assignments of an unchanged matrix/pair are controls. Positions, collision flags and hit-callback counts are retained. A world revision coalesces filter edits, and each controller invalidates its cache before its next move. Pair ignores participate in that revision. The unused controller mask copy is removed. |

The controller revision is an implementation choice that realizes the measured
next-move behavior. The captures do not establish how another engine stores its
contacts internally. Trigger enter/exit timing and further releases are outside
these added controls. No importer or serialized cache output changes.

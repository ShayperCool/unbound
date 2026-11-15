# Mesh data and light probe queries in jobs

Unity 6000.0.71f1, recorded twice with the macOS Editor in batch mode; the two captures are
byte-identical and the canonical reference has 49 rows. The values are job results (vertex data) and
safety exceptions and logs, so they do not depend on the OS or the graphics device. The gate
compares every missing, extra or unequal row exactly.

- A `Mesh.MeshDataArray` job field: an IJobParallelFor writing each index's own mesh runs; writing
  another index throws (logged from the job) `Index i is out of restricted IJobParallelFor range
  [j...j] in MeshDataArray.`, which `[NativeDisableParallelForRestriction]` lifts.
- A `[ReadOnly]` array field reads every mesh, and writing a vertex view throws `The
  UNKNOWN_OBJECT_TYPE has been declared as [ReadOnly] in the job, but you are writing to it.`: the
  array's MeshData and their vertex views share its safety handle.
- While a job writes the array, reading, indexing and disposing it on the main thread throw the
  job-conflict errors; a disposed array throws on indexing and on scheduling.
- A lone `Mesh.MeshData` job field may read but not write, and the job registers no use of it: the
  main thread reads and writes the same array while it runs.
- A `LightProbesQuery` job field is a container: disposing it while a read-write or `[ReadOnly]`
  job uses it throws the matching conflict, and a disposed query cannot be scheduled.

| Unity 6000.0 documentation | Discriminating control |
| --- | --- |
| [Mesh.MeshDataArray](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh.MeshDataArray.html), [Mesh.AllocateWritableMeshData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh.AllocateWritableMeshData.html) | `own.*`, `next.*`, `nextUnrestricted.*` and `applied`. |
| [LightProbesQuery](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/LightProbesQuery.html) | `query.*`. |

Not measured: other kinds of plain struct fields holding safety handles, and other releases.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/meshdatajobs/run.py native --label reference-a
python3 tests/projects/meshdatajobs/run.py native --label reference-b
python3 tests/projects/meshdatajobs/run.py publish reference-a reference-b
python3 tests/projects/meshdatajobs/run.py check
```

Captures, engine outputs and comparison reports stay in the run cache.

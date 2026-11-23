# Mesh skin weights

The Unity 6000.0 Scripting API accepts up to 255 influences per vertex supplied in descending weight
order. The probe checks count and index order through the borrowed arrays, input validation, and
the rounded weights that the selected layout stores. The six-bone bake moves each bone differently
so a four-influence result differs from the unlimited result. [SetBoneWeights](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh.SetBoneWeights.html) ·
[GetAllBoneWeights](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh.GetAllBoneWeights.html) ·
[GetBonesPerVertex](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh.GetBonesPerVertex.html)

Unity 6000.0.71f1 resizes the per-vertex counts as the geometry grows or shrinks and retains
the flat weight data from the last assignment. The legacy array follows the active vertices.
The probe also checks read-only views, their invalidation after a mutation, and the physical
vertex attributes and readable weights before, during, and after a rejected layout change.
Equal-weight rows verify that packing preserves descending order and that the borrowed arrays can
be passed back to the setter. A custom log handler verifies that the order diagnostic comes from
the engine and the rejected update completes its layout transition.

The GPU checks run after a frame has elapsed so a synchronous readback has a graphics context.
They compare raw words for every layout, including the unlimited layout's offset table and packed
influences. Separate wrappers share the buffer and can be disposed independently. The sequence
also checks immediately reading an existing buffer after a weight write, then requesting it again
to get the refreshed contents. [GetBoneWeightBuffer](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh.GetBoneWeightBuffer.html)

The advanced vertex-buffer checks replace an explicit skin layout and write both skin attributes.
The Editor uses the new stream for fixed CPU baking and retains the variable table for unlimited
skinning. The checks compare both a borrowed buffer and a newly requested view after the write.
The asset checks save and reload one-, two-, five-, and eleven-influence meshes, comparing the
variable table bytes and the reloaded arrays after an unsaved change. [SetVertexBufferParams](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh.SetVertexBufferParams.html) ·
[SetVertexBufferData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh.SetVertexBufferData.html)

The probe exercises scripted mesh weights. Imported meshes use the influences their importer
supplies. Synchronous readbacks issued before the renderer becomes available use the engine's
existing GPU availability diagnostic.

```sh
python3 tests/projects/meshskinweights/run.py record --release 6000.0.71f1 --label repeat-a
python3 tests/projects/meshskinweights/run.py record --release 6000.0.71f1 --label repeat-b
python3 tests/projects/meshskinweights/run.py publish --release 6000.0.71f1 repeat-a repeat-b
python3 tests/projects/meshskinweights/run.py gate --release 6000.0.71f1
```

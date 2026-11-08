# Model top-level node probe

Which node of an FBX file with a single top-level node becomes the model's root on Unity
6000.0.71f1, and which curves its take gets. `make_models.py` writes ASCII FBX 7.4 files (no
Blender needed): a top-level node `Top` with two children `ChildA` and `ChildB` whose translations
are animated over one take "Take 001" of one second, Y up, centimetres.

| Model | `Top`'s attribute | `DefaultAttributeIndex` | Other |
|---|---|---|---|
| NullTop | Null | absent | |
| NullTopDai0 | Null | 0 | |
| NullTopAnim | Null | absent | `Top`'s translation animated |
| NullTopCustom | Null | absent | turned 90° about X, a custom property animated |
| BareTop | none | absent | |
| SkelTopNoDai | Skeleton | absent | |
| SkelTopDaiM1 | Skeleton | -1 | |
| SkelTop | Skeleton | 0 | |
| SkelTopNullKids | Skeleton | 0 | Null children |
| SkelTopTurned | Skeleton | 0 | turned 90° about X |
| SkelTopTurnedAvatar | Skeleton | 0 | the same file with an avatar (`avatarSetup` 1) |
| SkelTopAnim | Skeleton | 0 | `Top`'s translation animated |
| SkelTopCustom | Skeleton | 0 | turned 90° about X, a custom property animated (as Maya exports `lockInfluenceWeights`) |

Children are Skeleton nodes where `Top` is one, except in SkelTopNullKids.

`TopRootProbe` records, for every model: the instantiated hierarchy (each transform's path, local
pose and components), its take's curve bindings and their values at 0, 0.5 and 1 s, and an
instance placed at (3, 4, 5) turned 30° that plays the take through `Animator.Update` (0, then
0.5 s), with root motion off and on: the root's world pose and every node's local pose.

The assets are committed. To make them again, regenerate the models and let a disposable Editor
copy write the `.meta` files and controllers back:

```bash
python3 tests/projects/modeltoproot/make_models.py
python3 tests/projects/modeltoproot/run.py create --release 6000.0.71f1
```

Record two independent Editor captures (GPU, private Xvfb), publish after they agree, run
Unbound and compare (modelroot's comparator: every missing, extra and differing value, numbers
within 2e-4, a rotation also matches its negation):

```bash
python3 tests/projects/modeltoproot/run.py editor --release 6000.0.71f1 --label <label>
python3 tests/projects/modeltoproot/run.py publish --release 6000.0.71f1 --label <label>
python3 tests/projects/modeltoproot/run.py unbound --release 6000.0.71f1
```

What the Editor does (measured):

- The single top-level node becomes the model's root (its transform moves to the root, its
  children become the root's) unless its default node attribute, the one
  `DefaultAttributeIndex` selects, is a Skeleton. A Skeleton attribute without a default index
  (absent or -1) does not keep it, a Null attribute with index 0 does not either. The node's
  rest rotation, its animation, its children's attribute and the avatar do not matter.
- A kept node is an ordinary child named after it; its children's curves are bound below it
  (`Top/ChildA`).
- A clip gets curves for the translation, rotation and scale the take animates (those with
  animation curves), constant ones too, and none for the others; a node whose take animates only
  custom properties gets no curves.
- Curves of the root (a collapsed top-level node's) are written as they are when root motion is
  off: the instance moves to the clip's position. With root motion on they move it relatively.

The Manual (6000.0, Model tab, Preserve Hierarchy: https://docs.unity3d.com/6000.0/Documentation/Manual/FBXImporter-Model.html)
only says the importer strips empty root nodes unless Preserve Hierarchy is set; it says
nothing about bones, and a mesh node is not empty yet becomes the root as well. The rule above
is this release's observed behaviour, not a documented guarantee.

Also checked by hand once (one observation, not reproducible from committed files and not part
of the reference): the same rule decides for a binary FBX from
Maya whose top-level LimbNode `Root` has `DefaultAttributeIndex` 0; patched to -1, it becomes the
root. The application that wrote the file, the node's name, its inheritance type, its
`RotationActive` and the `.meta`'s fileID tables do not change the result.

Not covered: a default attribute index pointing at another attribute than the first, mesh and
other attribute types as the default attribute of a node with several, Z-up files with a kept
top-level bone, nodes with only rotation or only scale animated, animation curve nodes without
curves, pivoted nodes, legacy and humanoid rigs (Unbound keeps every channel for these three)
and other releases.

# vfx probe

Visual Effect Graph (com.unity.visualeffectgraph 17.0.4, the Editor's built-in package, unmodified)
under URP 17: what the Visual Effect module answers about the probe's own small graphs, recorded by
the same script in the Unity Editor and in Unbound. Unbound imports a graph by running the
package's compiler (`managed/UnityEngine/Editor/VFX`); this probe covers the import and the API
that needs no simulation. Simulation and drawing are not implemented yet: Unbound warns once at
load and `unbound doctor` lists the effects.

## Graphs and scene

`Assets/Editor/Authoring/GraphAuthoring.cs` builds everything in the Editor through the package's
own models; its assembly carries the name of one of the package's friend assemblies
(`Unity.VisualEffectGraph.EditorTests`), which is what lets it reach them. The graphs:

- `Empty`: no contexts; it compiles to nothing.
- `Basic`: a spawner at a constant rate, initialize (lifetime, velocity), update, quad output.
- `Exposed`: one system and an exposed property of every type the blackboard offers (numbers,
  vectors, a color, spaceable positions in local and world space, a direction, a vector, a curve,
  a gradient, every texture dimension, a mesh, a skinned mesh renderer, a graphics buffer, a
  sphere, a box, a transform), with names that sort differently by culture and by code point,
  and one property that is not exposed.
- `Exposed2`: shares names with `Exposed`: `Rate` with the same type, `Count` with another.
- `Events`: two systems; custom events `Fire` and `Cease` start and stop the first spawner.
- `Attributes`: a system whose particles take `lifetime`, `velocity`, `color`, `alive` and `seed`
  from the spawn event, so the graph's event attributes have floats, vectors, a bool and an
  unsigned integer. `Attributes2`: `color`, `size` and `lifetime`, in another order.

`Exposed`'s texture and mesh properties default to `Assets/Objects/ImageA.png` and
`ShapeA.asset`, which the authoring script also makes (with `ImageB` and `ShapeB`).

`Assets/Scenes/Probe.unity` holds effects saved by the Editor: `Plain` (Basic), `Overridden`
(Exposed with overridden numbers, vectors, a curve, a gradient, a texture and a mesh, a start
seed, an initial event), `Events`, `NoAsset`.

`Assets/Editor/Authoring/RuntimeDataCases.cs` (the same friend assembly) hands a resource
compiled graphs made by hand, one case each: valid ones, system layers that decrease, are equal
or have a layerless system between them, expressions with an unknown operation, no result type,
a parent outside the sheet, a component outside its parent, a curve or gradient sampled from
another type, a word beyond an operation's parents outside the sheet, and an event named twice.

The Editor-authored files (graphs, scene, the package lock) are kept in
`Authored/unity-<release>/default` (`tests/authored_project.py`); the gate assembles them.

## What is recorded

`Assets/Scripts/VfxProbe.cs` writes `values.jsonl` (`{"k": key, "v": value}` per line; floats as
the hex of their bits) into `VFX_PROBE_OUT`:

- `tables/*`: the graph compiler's tables, read through reflection: the type every expression
  operation produces for each type a data word can name, the size of every value type (with the
  error logged for a type without one), its texture dimension; values outside the enums too;
  the runtime data version.
- `asset/<graph>/*`: exposed properties with their listed types (in the asset's order), each
  one's texture dimension and space, the same for an unknown name, events, the objects the import
  made with their file ids (compute shaders, the output shader and its material), the systems by
  kind.
- `scene/<effect>/*`: the serialized effects: asset, components, state, system names, and for
  every exposed property which typed `Has*` answer true, its value by the getter of its type and
  what that logged; an unknown name through several getters.
- `scripted/*`: an effect added from script: its defaults and renderer; every setter with the
  right type, a texture of another dimension, wrong types and unknown names (what is logged, what
  the value is afterwards); curves and gradients are copies; a clone carries the overrides;
  `ResetOverride`; another asset assigned and the first one again (which overrides follow), the
  same asset again, none and the first one after none (the overrides saved when the effect was
  cloned return, later ones do not), object overrides through an asset without those
  properties; state set from script and the initial event with and without an asset.
- `attributes/*`: event attributes per graph: which names and types the layout has, starting
  values, writes and reads with the right and a wrong type, copies, null arguments, use after
  `Dispose`; `attributes/layouts/*`: the two graphs with several typed attributes, written and
  read by type, and `CopyValuesFrom` in both directions between their layouts (shared names
  take the other set's values, the rest keep theirs).
- `runtime data/*`: what each hand-made compiled graph logs and throws when a resource takes
  it, and whether the resource holds the shader source handed over with it afterwards.
- `find/*`: which of the project's assets `AssetDatabase.FindAssets` lists for the graph types
  and for the types of the objects a compiled graph has (a shader, compute shaders, a material).
- `manager/*`: the fixed time step and the maximum delta time of the project's settings.

Values compare exactly (tolerance 0).

## Not covered yet

Simulation and drawing (spawners, particle counts, system info, events sent, output events,
culling, images): the members that need them are not implemented and say so when called.
One attribute name with different types in two graphs (a custom attribute) is not measured for
`CopyValuesFrom`. Whether a compiled graph a resource accepted would also evaluate (a word
beyond an operation's counted parents) is a matter of the simulation slices.
Everything was measured in the Linux Editor on the OpenGL Core device (NVIDIA driver); nothing
here depends on the device.

## Running

    python3 tests/projects/vfx/run.py record --label a     # one Editor capture (GPU display)
    python3 tests/projects/vfx/run.py record --label b
    python3 tests/projects/vfx/run.py publish a b          # identical captures -> Reference/ and Authored/
    python3 tests/projects/vfx/run.py gate                 # the engine on the authored project

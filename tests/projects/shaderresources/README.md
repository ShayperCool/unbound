# Buffers held in structs and many textures in one stage

`Assets/Scripts/ShaderResourcesProbe.cs` draws two probe shaders into a 4x4 linear render texture
with `Graphics.Blit` on the third frame and records one pixel of each case (bytes):

* **`member.*`** (`Assets/Shaders/BufferMember.shader`): a struct holding a `StructuredBuffer` and
  a `Texture2D`, filled from globals (as URP's Adaptive Probe Volume resources are), read through a
  function (`function`), through a copy of the struct (`copy`, one element further) and directly
  (`direct`), at indices 0 to 2 of a four-element buffer.
* **`many.*`** (`Assets/Shaders/ManyTextures.shader`): 24 textures sampled with one sampler state in
  one stage (URP's Lit with probe volumes binds 21 and shares sampler states among them): the last,
  the 17th and the mean of all; `many.24.changed` after one texture is replaced.

* **`gaps.color`** (`Assets/Shaders/UniformGaps.shader`): a material block whose members around
  one the vertex program alone reads are read by the fragment program (a uniform layout with
  rows only one stage uses). A control: it compiles with or without the type order fix below.
  The fitting of uniform blocks pads such gaps with members of new types, which naga appends
  after the block's struct; the types are put back in dependency order before the program is
  validated (unit test `fit::tests::padding_with_new_types_keeps_the_types_in_dependency_order`).
  The program this was found with (a Shader Graph water shader's fragment program in a keyword
  variant, whose merged uniform block lacked a row the other stage had) is not reproduced here:
  the probe's own blocks keep every member in both stages.

Measured in the Unity 6000.0.71f1 Editor (OpenGL Core, two identical captures on the NVIDIA
driver). The Editor's compiler refuses more than 16 sampler states in a stage (a `sampler2D` per
texture), so the textures share one.

    python3 tests/projects/shaderresources/run.py record --label A   # Editor capture (GPU)
    python3 tests/projects/shaderresources/run.py publish A B        # two identical captures
    python3 tests/projects/shaderresources/run.py gate               # the engine vs Reference/

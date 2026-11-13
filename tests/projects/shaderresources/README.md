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

Measured in the Unity 6000.0.71f1 Editor (OpenGL Core, two identical captures on the NVIDIA
driver). The Editor's compiler refuses more than 16 sampler states in a stage (a `sampler2D` per
texture), so the textures share one.

    python3 tests/projects/shaderresources/run.py record --label A   # Editor capture (GPU)
    python3 tests/projects/shaderresources/run.py publish A B        # two identical captures
    python3 tests/projects/shaderresources/run.py gate               # the engine vs Reference/

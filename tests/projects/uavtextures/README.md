# UAV Textures & Packed Storage Buffers Probe (`uavtextures`)

Measures Unity 6000.0.71f1 behavior for:

1. **Fragment and compute `<4`-channel `RWTexture*` writes into 4-channel `RenderTexture`s**
   (`R16G16B16A16_SFloat`, `R8G8B8A8_UNorm`, `R32G32B32A32_UInt`):
   - `RWTexture2D<float>` (`0.0`, `0.5`, `1.0`), `RWTexture2D<float2>`, `RWTexture2D<float3>`,
     `RWTexture2D<float4>`, `RWTexture2D<uint>` (`0`, `7`), and `RWTexture3D<float>` (`0.0`,
     `0.5`, `1.0` at `register(u4)` with `ColorMask 0`, `ZClip Off`).
   - Measured in both Unity 6000.0.71f1 OpenGL Core and Vulkan (two identical captures each):
     both backends and both stages (fragment and compute) agree byte-for-byte that missing
     channels are filled with channel `.x` (`v.xxxx`, `v.xyxx`, `v.xyzx`).
2. **Fragment random-write texture binding lifecycle**:
   - `Graphics.SetRandomWriteTarget` / `ClearRandomWriteTargets`, `CommandBuffer.SetRandomWriteTarget`,
     texture-to-texture, buffer-to-texture, and texture-to-buffer slot replacement, combined
     explicit `RWTexture2D : register(u1)` + auto-assigned `RWStructuredBuffer` (`u2`), and
     fragment read-modify-write (`_Out[xy] = _Out[xy] + delta`).
3. **Vertex `switch` with `default:` falling through into `case 0:`** (`switch.axis0..2`).
4. **Tightly packed `StructuredBuffer<T>` layouts** (`TrailingPad { float4; float; }`,
   `MixedVec4 { float3; float4; float; float4; }`, nested `Outer { Inner { float4; float; }; float; }`,
   and a co-resident std430-aligned `Aligned { float4; }`).

## Reference

`Reference/unity-6000.0.71f1.jsonl` was published from two byte-identical Unity 6000.0.71f1
Linux Vulkan captures (`run.py native --label L --vulkan`, `run.py publish L-1 L-2`).
OpenGL Core Unity 6000.0.71f1 agrees on 48/50 keys (all 34 `<4`-channel expansion cases,
all slot replacement, mixed UAV, switch, and packed-struct cases); Unity's OpenGL Core backend
retains a stale GL image unit after `Graphics.ClearRandomWriteTargets` when drawing with an
unbound UAV (`lifecycle.gfx.cleared`) and reads zeros before an explicit image barrier in
`lifecycle.rmw`. Unbound's wgpu renderer matches Vulkan on both OpenGL Core and Vulkan backends.

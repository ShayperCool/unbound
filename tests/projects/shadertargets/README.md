# shadertargets

Which shader target and GPU features each keyword variant of a graphics program compiles with,
under its `#pragma target` and `#pragma require` lines, with and without keywords, and the
built-in keywords that bring requirements of their own.

Each case is a shader `Probe/Targets/<case>` (`Assets/Shaders`) declaring two keywords, drawn in
each of its variants into one float texel with `Graphics.Blit` (`Assets/Scripts/ShaderTargetsProbe.cs`).
The program (`TargetsCommon.hlsl`, no includes) writes `SHADER_TARGET` in red, one bit per
`SHADER_AVAILABLE_<FEATURE>` define in green, one bit per probe keyword the variant has in blue
and `SHADER_TARGET_AVAILABLE` in alpha. Rows are `<case>.<keywords>` (`none`, or the material's
enabled keywords joined by `+`).

| Case | Lines |
| --- | --- |
| None | none (2.5) |
| Plain | `target 3.5` |
| TwoPlain | `target 4.5`, `target 3.0` |
| Keyed | `target 2.0`, `target 4.5 KW_A`, `target 3.5 KW_B` |
| KeyedFirst | `target 4.5 KW_A`, `target 3.0` |
| KeyedOnly | `target 4.0 KW_A` |
| KeyedLower | `target 4.5`, `target 3.0 KW_A` |
| TwoKeywords | `target 4.6 KW_A KW_B` |
| Target20 ... Target50 | `target 2.0`, `3.0`, `4.0`, `4.6`, `5.0` |
| ReqCube, ReqKeyed | `target 3.5` + `require cubearray`; `require cubearray : KW_A` |
| ReqCompute, ReqInts | `require compute randomwrite`; `target 2.0` + `require integers` |
| Target46Cube | `target 4.6` + `require cubearray` |
| ReqMsaaKeyed | `target 2.0` + `require msaatex : KW_B` |
| BuiltinKeywords | `multi_compile_instancing`, `multi_compile _ SHADOWS_SCREEN`, `target 2.0` |
| BuiltinCompute | `multi_compile _ PROCEDURAL_INSTANCING_ON`, `multi_compile _ DYNAMICLIGHTMAP_ON` |
| Aliases | `target es3.0`, `target gl4.1 KW_A`, `target es3.1 KW_B` |
| BuiltinShadows | `multi_compile _ SHADOWS_SOFT`, `multi_compile _ DIRLIGHTMAP_COMBINED`, `target 2.0` |
| ReqMrt4Array, ReqInstSparse | `target 2.0` + `require mrt4 : KW_A`, `require 2darray : KW_B`; `require instancing : KW_A`, `require sparsetex : KW_B` |
| ReqStages, ReqComputeTess | `target 2.0` + `require geometry : KW_A`, `require tessellation : KW_B`; `require compute : KW_A`, `require tessellation : KW_B` |

The Unity 6000.0 Manual (Targeting shader models and GPU features in HLSL) documents that a line
with keywords applies to the variants with any of them. What the captures show on both devices
(Unity 6000.0.71f1, NVIDIA driver only):

- a variant's target is the highest of the lines that apply to it, in any order; without a plain
  `#pragma target` it is 2.5; a plain 4.0 compiles as 3.5 and a plain 4.6 or 5.0 as 4.5 (the
  geometry and tessellation stages' features are left out of a program without those stages),
  while a line with keywords keeps its target (`target 4.0 KW_A` is 4.0 for `KW_A`);
- required features raise the target on their own: `mrt4` to 3.0, `2darray` and `instancing` to
  3.5, `geometry` to 4.0, `compute` and `msaatex` to 4.5, `tessellation` to 4.6, `compute` with
  `tessellation` to 5.0; `integers` and `cubearray` raise nothing; `SHADOWS_SCREEN`,
  `SHADOWS_SOFT`, `DIRLIGHTMAP_COMBINED` and `DYNAMICLIGHTMAP_ON` make it 3.0 and
  `PROCEDURAL_INSTANCING_ON` 4.5; a material's `INSTANCING_ON` selects no variant (the draw
  decides instancing);
- a variant requiring `sparsetex`, which neither device has, draws with the error shader
  (magenta);
- the `SHADER_AVAILABLE_*` defines hold the variant's features and the device's own, 3.5's on
  OpenGL Core and 4.5's on Vulkan, never the stages' (`TESSHW`, `GEOMETRY`, `TESSELLATION`) and
  cube arrays only through `#pragma require cubearray` (4.6 and 5.0 do not bring them);
  `INTERPOLATORS15` and `INTEGERS` come together, `require uav` defines `RANDOMWRITE` alone;
  `SHADER_TARGET_AVAILABLE` is those features' target (35 or 45);
- `es3.0`, `es3.1` and `gl4.1` targets apply nothing on these devices.

Where the captures differ from the Manual's pragma target reference: it gives 4.6 cube arrays
and describes `gl4.1` as 4.0 with cube arrays, tessellation and MSAA texture reads; on these
devices 4.6 and 5.0 bring no cube arrays and `gl4.1` applies nothing.

Unmeasured: the instancing variant's target (an instanced draw of the case recorded nothing in the
Editor; taken as the instancing feature's, 3.5), a material's `INSTANCING_ON` through a renderer's
draw (only `Graphics.Blit` is recorded), compute kernels (5.0 without the stages' features), the
features other than sparse textures a device may lack (wave, 64-bit, 16-bit, barycentrics, ray
tracing: taken as present), the stereo and device capability keywords (Unbound enables none),
targets Unity does not name (left out, as the platform ones are).

    python3 tests/projects/shadertargets/run.py native --label L [--device vulkan]   # two Editor captures
    python3 tests/projects/shadertargets/run.py publish L-1 L-2 [--device vulkan]
    python3 tests/projects/shadertargets/run.py check [--device vulkan]              # the land gate

`Reference/unity-6000.0.71f1/{opengl,vulkan}.jsonl` are two identical Editor captures each.
Unbound matches every record on both devices.

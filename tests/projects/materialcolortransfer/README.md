# Material color transfer oracle

Unity 6000.0.71f1 on Linux, NVIDIA OpenGL Core. The shader returns one property
without arithmetic into a 4x4 single-sample ARGBFloat linear target. All uniforms,
fragment output, and CPU readback use full float precision. There are no cameras,
effects, blends, screen reads or half/fixed shader variables. A command-buffer mesh
draw tests material and property-block bindings through the same path.

CPU getters are recorded before and after each draw, independently of all GPU
channels. The probe covers Color, HDR Color, Vector, Gamma Color, Gamma HDR Color,
Gamma Vector, Gamma Float and ordinary Float declarations. Shader defaults,
Editor-created imported material and material variant assets, repeated SetColor
and SetVector assignments, material copies, property-block copies, shared material
identity and live parent/variant overrides are separate cases. Finite values
cover transfer-function boundaries, [0,1], HDR RGB/alpha and negative components.
Mathf conversions and Color.linear/gamma have separate CPU-only boundary probes.
Nonfinite inputs are API-only and do not certify any GPU behavior.

`compare.py` fixes absolute and relative tolerances at 2e-5 before recordings or
runtime edits. Every observation key, image key and all 64 float channels per
image are mandatory. There are no expected failures or exclusions.

The runner accepts MATERIAL_COLOR_TRANSFER_CLI, MATERIAL_COLOR_TRANSFER_NATIVE_DIR,
UNBOUND_MANAGED_DIR, MATERIAL_COLOR_TRANSFER_BUILD, MATERIAL_COLOR_TRANSFER_PROJECT
and MATERIAL_COLOR_TRANSFER_OUT. Defaults use the frozen native6 CLI/library and
private managed build directories. All heavy jobs use the global lock and disable
shared compilation/build servers. Gamma runs use a copy below this fixture's
Library, changing only that copy's m_ActiveColorSpace to 0.

Measurement commands, evidence and results will be recorded after capture.

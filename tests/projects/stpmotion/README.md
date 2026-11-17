# STP upscaling with a still and a moving camera

URP 17's Spatial-Temporal Post-processing upscaler (STP,
https://docs.unity3d.com/6000.0/Documentation/Manual/urp/stp/stp-upscaler.html) renders at a
lower resolution and reconstructs the output from the frames before it: each frame's camera jitter,
motion vectors and depth decide how much of the history it keeps. The Manual documents that STP
needs compute shaders and that it is a temporal technique (its image depends on the frames before
the recorded one); it does not specify its internal weights. This probe checks the whole STP chain
(Setup, Pre-TAA and TAA kernels, history textures and URP's constants) against the Editor while the
camera moves, which `tests/projects/urp`'s `upscale_stp` (a still camera, Gamma color space) does
not cover.

`ProbeRunner.Create` (run once; its outputs are committed) made a 64x64 checker texture with two
diagonal lines, URP Unlit materials of it, and the probe scene: a ground plane, a wall and seven
posts in front of a camera with a solid background. The project is in Linear color space with HDR
on; the pipeline asset and renderer are the graphqueue probe's (Forward, no renderer features).

`ProbeRunner.Record` plays the scene. `StpMotionProbe` sets `Time.captureFramerate` to 30 (STP
scales its motion matching by this frame's delta time over the last one's), turns post-processing
on with no camera anti-aliasing and renders into a 320x180 render texture (1280x720 where named).
Each scenario sets the render scale and upscaling filter, puts the camera at its start, resets the
camera's history, renders one frame, then moves the camera by a fixed step (and turns it by a fixed
yaw) every frame for 24 frames and records the last frame as a PNG; `values.jsonl` records the frame
it was rendered in.

- `stp_static`, `stp_pan`, `stp_turn`: render scale 0.5, still, sideways, turning (0.37 degrees a
  frame);
- `stp_pan_08`: render scale 0.8 (as a large scene uses it), moving sideways, up and forward while
  turning;
- `linear_pan`: the Linear upscaler (no history) with the same pan, a control;
- `stp_pan_far`: near and far planes of 1 and 1500 (STP packs depth logarithmically between them);
- `stp_pan_prime`: Forced depth priming and the Forward+ path;
- `stp_pan_noise`, `stp_static_noise`: a hashed value-noise texture in place of the checker
  (texture detail of a natural scene rather than flat squares), moving and still;
- `stp_turn_fast`, `stp_turn_fast_noise`, `stp_pan_fast`: several pixels of motion a frame
  (1.6 degrees of yaw, or 0.11 units sideways);
- `stp_pan_offset`, `stp_static_offset`: the scene and camera 150, 60, 120 units from the world's
  origin (STP's view matrices hold the camera's world position);
- `stp_turn_fast_720`, `stp_turn_fast_noise_720`: a 1280x720 target.

Values compare exactly; `image_limits.json` holds each image's measured limits.

Measured in the Unity 6000.0.71f1 Editor (Linux, OpenGL Core, NVIDIA driver; two identical
captures): Unbound on its OpenGL Core device renders every scenario within the limits, most to the
pixel. A few isolated edge pixels differ where noted. This parity on synthetic content does not
extend to every scene: docs/COMPATIBILITY_GAPS.md (Q546) records a large URP scene whose STP output
under camera motion keeps more of its history in Unbound than in the OpenGL Core Editor. Other
devices, GPUs and drivers are unmeasured.

    ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -quit -projectPath <copy of this probe> \
        -executeMethod ProbeRunner.Create -logFile create.log   # once; copy Assets/ back
    python3 tests/projects/stpmotion/run.py record --label A    # and B; then publish A B
    python3 tests/projects/stpmotion/run.py gate

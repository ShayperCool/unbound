# OnRenderObject order witness

Which order does Unity call `OnRenderObject` handlers in when a camera renders? The core API
probe (`tests/projects/coreapis`, w4 reference) shows one fixed list, RtCam, ScreenCam, Cube,
for every camera render. It rules out hierarchy order, current-camera-first and calls to the
camera's own object only. It cannot separate the remaining explanations, because in that scene
they all predict the same list. This probe separates them with deliberate, recorded mutations.

## Project

This is the core API probe's project, copied unchanged: the scene `Probe.unity`, its assets,
the `RenderWatch` handlers on RtCam, ScreenCam and Cube, `ProbeRunner` (windowed Editor, 320x240
Game view, Play mode, exit from Edit mode), and all of CoreProbe's scenarios and its
OnDemandRendering window w0..w16. Only `CoreProbe.cs` differs:

* the record continues after w16 with an order window, frames o0..o16 (w17..w33), and the
  probe finishes at w34 instead of w18;
* in the order window, every frame records its phase, the state it renders with (in LateUpdate:
  each handler's `enabled`/`isActiveAndEnabled`, each camera's `enabled`, depth and target),
  and every deliberate mutation with its read-back;
* o0 records the instance-id order of the three handlers, their GameObjects and the two cameras,
  as ranks and signs. The raw ids go to `values.jsonl.ids.txt`, which is not compared;
* the record ends with `{"k":"complete","v":true}` and is written and closed before the Editor
  leaves Play mode.

No added code implements `OnRenderObject`. The camera render sequence is the
`Camera.onPreCull` lines, which CoreProbe already records. The handler order of each render is
the `OnRenderObject cam=<camera>` lines, in record order.

## Phases (fixed before the first capture)

Mutations run in CoreProbe's Update on a phase's first frame, before that frame renders. Each
phase lasts two frames; P5 lasts three (o14..o16).

| frame | phase | mutation | cameras (RtCam / ScreenCam) |
|---|---|---|---|
| o0 | settle | renderFrameInterval = 1 (applies from o1); instance-id order | texture -5 / screen 0 |
| o2 | P0 baseline | none | texture -5 / screen 0 |
| o4 | P1 mixed-target depth | ScreenCam.depth = -10 | texture -5 / screen -10 |
| o6 | P2 offscreen depth (camera-order control) | ScreenCam.targetTexture = a 64x64 texture | texture -5 / texture -10 |
| o8 | P2b offscreen restore | ScreenCam.depth = 0 | texture -5 / texture 0 |
| o10 | P3 restore | ScreenCam.targetTexture = null | texture -5 / screen 0 |
| o12 | P4 re-enable control | RtCam's RenderWatch.enabled = false, then = true | texture -5 / screen 0 |
| o14 | P5 combined | ScreenCam.targetTexture = texture, ScreenCam.depth = -10 | texture -5 / texture -10 |

P1 is the audit's mixed-target depth change. The built-in pipeline may render offscreen
cameras first whatever their depth, so P1 may leave the camera order unchanged. It is
therefore not relied on. P2 and P5 change the order within one target class (both cameras
render into textures). That is a test mutation, not a targetTexture sorting rule. Whether the
camera order actually changed is read from the record. If it did not change, P2/P5 do not
separate the camera-order hypotheses, and the verdict says so.

## Hypotheses and predictions

R = RtCam, S = ScreenCam, C = Cube. B is the observed P0 order. The registration hypotheses
take B as Unity's scene-load registration order, which this probe does not witness separately.

* **H1 registration (append)**: one list in OnEnable order; disabling removes and keeps the
  order of the rest; enabling appends.
* **H1s registration (swap-remove)**: the same, but removal moves the last entry into the gap.
* **H2 instance id / load order**: the handlers' instance-id order (recorded at o0), fixed at
  load. **H2f**: Probe.unity's fileID order R, S, C.
* **H3a camera render order first**: the camera handlers in this frame's actual camera
  render order, then the others (C). **H3b**: the same in camera depth order.
* **H4 type-grouped registration**: per-type lists in registration order. The probe has a
  single handler class, so H4 predicts what H1/H1s predict. It stays unresolved.
* H0 current camera first (already ruled out; kept as a check).

Predicted handler order of every camera render. The camera order shown is expected; the
record gives the actual one, which is what H3a uses.

| phase | expected cameras | H1 | H1s | H2 / H2f | H3a | H3b |
|---|---|---|---|---|---|---|
| P0 | R, S | B (RSC) | B (RSC) | RSC | RSC | RSC |
| P1 | R, S (offscreen first) or S, R | RSC | RSC | RSC | follows actual | SRC |
| P2 | S, R | RSC | RSC | RSC | SRC | SRC |
| P2b | R, S | RSC | RSC | RSC | RSC | RSC |
| P3 | R, S | RSC | RSC | RSC | RSC | RSC |
| P4 | R, S | SCR | CSR | RSC | RSC | RSC |
| P5 | S, R | SCR | CSR | RSC | SRC | SRC |

P4 alone separates registration from the fixed orders. P2 alone separates camera order from
load order. P5's predictions are all distinct. A registration result says the list follows
OnEnable at run time. It does not by itself show that Unity activates scene objects in file
order: the scene-load wake order of these handlers is not recorded.

## Guards

A capture backs the reference only if all of these hold:

* the Editor exited 0 with no forced stop of any kind (not even the shutdown guard), on the
  NVIDIA renderer;
* `{"k":"complete","v":true}` is the single last line, right after the finish record;
* every order-window frame has its frame and state lines, and exactly the mutations above with
  their expected read-backs;
* the three instance-id order lines are present;
* no exception, error or `step threw` line follows the OnDemandRendering window.

The observed order never enters a guard. The second capture is started only after the first
passes. Publishing needs both captures byte-identical. Whether the record reproduces the core
API w4 reference up to w16 is reported (`analyze.py --context`) but does not gate publication.

```
python3 tests/projects/oroorder/run.py editor --release 6000.0.71f1 --label <new> --repeat native1
python3 tests/projects/oroorder/run.py check  --release 6000.0.71f1 --label <new> --repeat native1
python3 tests/projects/oroorder/run.py editor --release 6000.0.71f1 --label <new> --repeat native2
python3 tests/projects/oroorder/run.py publish --release 6000.0.71f1 --label <new>
python3 tests/projects/oroorder/analyze.py tests/projects/oroorder/Reference/unity-6000.0.71f1.jsonl \
    --context tests/projects/coreapis/Reference/unity-6000.0.71f1.jsonl
```

# Stacked screen cameras

What the Editor's Game view (160x90, built-in RP) shows of three orthographic screen cameras
(`Assets/Scripts/StackProbe.cs`):

- A (depth 0, full rect, solid clear): an opaque red quad 4 units away.
- B (depth 1): the case's clear flags (Depth only, Don't Clear), rect (full, partial), HDR,
  MSAA and rendering path. An opaque HDR-intensity green quad 8 units away (behind A's red one),
  a half-transparent blue quad 2 units away and an opaque cyan quad 5 units away.
- C (depth 2, Don't Clear, LDR, right-hand rect): a yellow quad 6 units away over its whole
  view. Where yellow is hidden, the screen kept a nearer depth (A's red, B's cyan).

Cases: quality AA 0 and 4, A LDR/HDR (and without MSAA), B's clear/rect/HDR/MSAA, B first on
the screen (A off), B deferred (orthographic `.def`, which renders Forward, and perspective
`.def.persp`, which renders deferred, at quality AA 0 and 4), B clearing its color (solid, full
and partial), and a partial B under a full-screen Don't Clear C (`.cF`, drawing yellow over its
left half only). Each case starts with one frame in which A alone clears the
screen to magenta (color and depth). The Game view is captured after the case's first (`f1`) and
fourth (`f4`) frames, so `f4` also shows what the case's earlier frames left on the screen.

Measured in 6000.0.71f1 (Linear and Gamma; both repeats byte-identical):

- The screen keeps its color and depth from frame to frame: a first camera that clears only depth
  or nothing draws over the previous frame (the Manual's Don't Clear smear).
- Consecutive full-screen cameras that keep the color render into the first one's intermediate,
  with its depth. The stack is HDR when any of its cameras is. It takes the most samples any of
  them asks for: a camera with `allowMSAA` off is antialiased under or over one with it on.
- A Don't Clear full-screen intermediate starts from the screen's depth.
- After a full-screen intermediate is resolved, a camera that draws on the screen itself sees no
  depth.
- A partial camera with HDR, or with MSAA under quality AA, renders into an intermediate of its
  pixel size, antialiased. A Don't Clear one starts with cleared depth and leaves the screen's
  depth alone.
- A color-clearing B inside the stack, and a partial B between full-screen A and C, follow the
  same rules (no case of its own).
- Deferred B (A in LDR; the orthographic `.def` cases render Forward, `bPath`; the perspective
  `.def.persp` cases at quality AA 0 and 4 render deferred): clearing only depth or nothing, its
  light buffer starts from the color under it (the screen's); full screen, a Don't Clear one starts
  from the screen's depth and the stack it continues renders without MSAA (q4 equals q0); a partial
  one renders at its pixel size.

These are observations of this release and context (Linux Editor, OpenGL Core, NVIDIA), not
documented guarantees.

Metal (`Reference/unity-6000.0.71f1/metal/<space>`, the macOS Editor's Game view on an Apple M2
Pro, windowed, two identical captures per color space; macOS checks compare with it). The values
are the Linux ones; 62 Linear and 10 Gamma images differ from the Linux ones:

- In Linear color space a partial camera's HDR intermediate (`part.bH`, `part.bHn`, also a deferred
  one's light buffer, `part.bH.def*`, and B first on the screen, `first.*.part.bH`) that keeps the
  color under it (Depth only, Don't Clear) starts from the screen's stored bytes read as linear
  values: after the resolve that color shows encoded twice (the clear color 26/26/76 becomes
  88/88/149). Its LDR and full-screen intermediates start from the decoded colors, as on Linux.
- In both color spaces the full-screen LDR B without antialiasing (`q0.*.full.bL`, 10 images)
  covers one more row of pixels at three quads' top edges (rows 13, 31 and 49 from the top: the
  device's coverage of an edge through pixel centres).

Unbound on Metal: 192/192 images and 387/387 values in both color spaces. A Don't Clear deferred
camera over the screen takes the screen's depth with the rows the right way up (before, its
`full.bH.def.persp` images drew B where A's red quad hides it).

Vulkan (`Reference/unity-6000.0.71f1/vulkan/<space>`, `--device vulkan`: the Linux Editor with
-force-vulkan on the GPU-backed display, two pairs of captures per color space, the values the
same in all four). A Don't Clear deferred camera over the screen takes the screen's depth the
right way up, as on Metal (`*.full.bH.def.persp`; Vulkan's screen is top-down too). In the cases
where an LDR A is followed by a B that clears only depth or nothing (`q0.aL.bD.*`, `q0.aL.bN.*`,
14 Linear and 12 Gamma images) the Vulkan Editor draws some objects in its error color, and
which ones changes from capture to capture: every frame of such a case is listed in the
reference's `unstable.json` and not compared (`compare.py` reports them). Unbound on Vulkan:
178/178 Linear and 180/180 Gamma images, 387/387 values in both.

Run from the repository root; the runner takes one launcher lease per Editor through
`tests/editor_runner.py` (do not wrap it in `with-heavy-job`):

    python3 tests/projects/camerastack/run.py editor  --release 6000.0.71f1 --space Linear --label L-Linear
    python3 tests/projects/camerastack/run.py publish --release 6000.0.71f1 --space Linear --label L-Linear
    UNBOUND_CLI=$UNBOUND python3 tests/projects/camerastack/run.py unbound --release 6000.0.71f1 --space Linear --label U-Linear
    python3 tests/projects/camerastack/compare.py tests/projects/camerastack/Reference/unity-6000.0.71f1/Linear \
        ~/.cache/unbound/validation/runs/camerastack/U-Linear/unbound

    # Vulkan: two pairs per color space, published together (every capture must agree on a case)
    python3 tests/projects/camerastack/run.py editor  --release 6000.0.71f1 --space Linear --device vulkan --label V-Linear
    python3 tests/projects/camerastack/run.py editor  --release 6000.0.71f1 --space Linear --device vulkan --label V2-Linear
    python3 tests/projects/camerastack/run.py publish --release 6000.0.71f1 --space Linear --device vulkan --label V-Linear --also V2-Linear

`compare.py` checks every value and every case image (a pixel differs when a channel is more than
3/255 off) and exits 1 on any missing, extra or failing one.

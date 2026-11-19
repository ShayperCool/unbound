# MeshCollider cooking, 2D effectors' layers, VideoClip audio tracks

Three facts the gap map listed as source-ready but unmeasured, in one probe. Unity 6000.0.71f1,
recorded with the macOS Editor in batch mode (two byte-identical captures); none of it depends on
the OS or the graphics device.

`Assets/Editor/CookingForcesVideoProbe.cs`, in Play mode:

* **MeshCollider cooking** (Scripting API 6000.0,
  [MeshColliderCookingOptions](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MeshColliderCookingOptions.html)):
  a mesh on the y = 0 plane with three triangles sharing a corner given twice, a degenerate
  triangle and a vertex no triangle uses, cooked with each option combination (none, the default
  30, each option off, cleaning only, welding only, the obsolete InflateConvexMesh bit), as a
  triangle mesh and as a convex hull; after the options change; from a mesh whose data was
  released (`UploadMeshData(true)`); after `Physics.BakeMesh`. Each case records the options read
  back, rays down and up at six points (hit, distance, triangle index, normal, barycentric
  coordinate) and the collider's bounds.
* **2D effectors**: an Area, Point, Surface and Buoyancy effector on layer 8 with two bodies, A on
  layer 9 and B on layer 10, stepped 25 times by `Physics2D.Simulate(0.02)`: no mask, the
  effector's collider mask with A's layer, the layer collision matrix leaving B out, B's collider
  excluding the effector's layer, B's force receive layers and the effector's force send layers
  leaving each other out, and the mask with B's collider including the effector's layer. Each
  body's motion and whether the effector acted on it.
* **VideoClip audio**: a VP8 WebM clip with two Vorbis tracks (stereo 44.1 kHz "eng", mono
  22.05 kHz "fra") and one without audio: `VideoClip.audioTrackCount`, each track's channels,
  sample rate and language (one index past the tracks), a VideoPlayer's tracks, controlled
  track count and limit after the clip is set and after preparation.

Measured: Enable Mesh Cleaning or Weld Colocated Vertices drops an unused vertex from the cooked
mesh (its bounds), welding changes which triangle a ray at the shared corner reports, a convex
hull reads no option, the obsolete bit reads back; an effector's mask, the collision matrix and
the colliders' exclude layers keep an effector off a body, a mask leaving a layer out wins over
the body's include layers, force send and receive layers do not stop an effector (a surface's
body that receives no force from it falls through while it is carried); a player given a clip
controls all its audio tracks.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/cookingforcesvideo/run.py record --label native-a
python3 tests/projects/cookingforcesvideo/run.py record --label native-b
python3 tests/projects/cookingforcesvideo/run.py publish native-a native-b
UNBOUND=/path/to/unbound python3 tests/projects/cookingforcesvideo/run.py gate
```

The clips were made with ffmpeg 8 (libvpx, libvorbis):

    ffmpeg -f lavfi -i testsrc2=size=64x48:rate=10 -f lavfi -i sine=frequency=440:sample_rate=44100 \
        -f lavfi -i sine=frequency=220:sample_rate=22050 -t 1 -map 0:v -map 1:a -map 2:a -c:v libvpx -b:v 200k \
        -c:a libvorbis -ac:a:0 2 -ac:a:1 1 -metadata:s:a:0 language=eng -metadata:s:a:1 language=fra twotracks.webm
    ffmpeg -f lavfi -i testsrc2=size=64x48:rate=10 -t 1 -c:v libvpx -b:v 200k silent.webm

Values compare exactly. Unbound on macOS, 2026-11-19: before 444/482 (the welding-only bounds,
the mask against include layers, the controlled track count, and the clips without .meta files),
after 480/482 with 2 recorded residuals: force send and receive layers are not applied to
contacts (reported through Stubs.Ignored).

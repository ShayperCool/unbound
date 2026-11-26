# VP8 video with alpha

The Unity 6000.0.71f1 Play mode capture (batch Linux GPU Editor, two identical captures) shows
frame 0 of `Assets/Video/alpha.webm` (VP8 with an alpha channel, 64x48, 10 frames a second, one
second, one colour 0x4080C0, its alpha a horizontal ramp from 0 at the left column to 255 at the
right) paused in an ARGB32 and an ARGBHalf render texture, and records the colour and alpha
(0..255) at columns 0, 8, ..., 56 and 63 of the middle row. The Editor gives the alpha as decoded
(full range: 0, 32, 64, ..., 254), as Hidden/VideoDecode's YCbCrA_To_RGBAFull pass does.

The clip was made with:

    ffmpeg -f lavfi -i "color=c=0x4080C0:size=64x48:rate=10,format=yuva420p,geq=lum='p(X,Y)':cb='p(X,Y)':cr='p(X,Y)':a='X*255/63'" \
        -t 1 -c:v libvpx -pix_fmt yuva420p -auto-alt-ref 0 -b:v 1M alpha.webm

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/videoalpha/run.py record --label native-a
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/videoalpha/run.py record --label native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/videoalpha/run.py publish native-a native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/videoalpha/run.py gate
```

The probe uses the video probe's scene, runner, packages and settings; `residuals.json` records
what Unbound does not reproduce, with its cause.

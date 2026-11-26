# VideoPlayer from http URLs

The Unity 6000.0.71f1 Play mode capture (batch Linux GPU Editor, two identical captures) plays
http URLs the runner serves on 127.0.0.1 while the Editor or the engine runs (`VIDEO_URL_BASE`):
the video probe's `clip.webm` (no audio) and the video audio probe's `tone.webm` (one Vorbis
track), a file the server does not have, and a refused connection (`http://127.0.0.1:1/`). For
each, a fresh player rendering into a render texture records its answers before Prepare, right
after it (isPrepared is still false: preparing is asynchronous), and once prepared or failed (the
errors received: "VideoPlayer cannot play url : <url>", a blank line and "Cannot read file." for
both failures); frame 5 of `clip.webm` is sought and sampled. Messages show the server's address
as `BASE/` and the refused one as `REFUSED/`.

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/videourl/run.py record --label native-a
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/videourl/run.py record --label native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/videourl/run.py publish native-a native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/videourl/run.py gate
```

The probe uses the video probe's scene, runner, packages and settings.

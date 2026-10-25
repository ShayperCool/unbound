# Video probe

A VideoPlayer playing `Assets/Video/clip.webm` (ffmpeg's `testsrc2`, 64x48, 10 frames a second,
3 seconds, VP8: the Linux Editor does not import H.264 MP4) into a RenderTexture. `VideoProbe`
records the clip's properties, the prepared player, pausing (which shows frame 0), seeking to
frames 0, 1, 5, 14 and 29 (a 3x3 grid of pixel samples and a PNG of each), stepping past the end,
and playing from the start to the end (events, state).

The clip was made with:

    ffmpeg -f lavfi -i testsrc2=size=64x48:rate=10 -t 3 -c:v libvpx -b:v 1M tests/projects/video/Assets/Video/clip.webm

Record Unity's reference (the Editor needs a display):

    ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -quit -projectPath tests/projects/video \
        -executeMethod ProbeRunner.Create -logFile ~/.cache/video-create.log
    VIDEO_PROBE_OUT=$PWD/tests/projects/video/Reference \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/video \
        -executeMethod ProbeRunner.Record -logFile ~/.cache/video.log

Run Unbound and compare (Unbound decodes through the machine's `ffmpeg`; frames are uncapped, so
the probe needs many of them to play 3 seconds):

    VIDEO_PROBE_OUT=/tmp/video unbound run -p tests/projects/video --frames 30000 --size 320x180 --no-watch
    python3 tests/projects/video/compare.py tests/projects/video/Reference /tmp/video
    python3 tests/projects/particles/compare_images.py tests/projects/video/Reference /tmp/video

Every record matches. The decoded colours are ffmpeg's (BT.601, limited range), about 2/255 from
the Editor's on average, but up to ~80/255 at chroma edges, where the Editor converts YUV its own
way; one of the 9 samples lies on such an edge. None of ffmpeg's scalers, matrices or chroma
placements tried came closer.

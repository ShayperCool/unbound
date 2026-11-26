# VideoPlayer audio output

The Unity 6000.0.71f1 Play mode capture (batch Linux GPU Editor, two identical captures) plays
`Assets/Video/tone.webm` (VP8 64x48, 10 frames a second, 3 seconds, a Vorbis stereo track at 48
kHz: a tone stepping every half second through 300, 450, 600, 750, 900 and 1050 Hz, its left
channel at amplitude 0.5 and its right at 0.25) with a VideoPlayer rendering in API Only mode,
one fresh player and AudioSource per scenario:

- `play`: AudioSource output to the end of the clip; `volume`: the source at volume 0.5;
  `pause`: paused for half a second, then played again; `seek`: paused, sought to 2 seconds,
  played; `speed2`: playbackSpeed 2; `trackOff`: the track disabled; `direct`: Direct output;
  `none`: no audio output; `loop`: looping past the end.
- A filter on the AudioSource (OnAudioFilterRead) measures every block it is given. Each phase
  records whether any sound came, the tone steps heard in order (0 for 300 Hz to 5 for 1050 Hz; a
  step counts when two blocks in a row hear it), and the steady blocks' RMS per channel; `.listener`
  is whether AudioListener.GetOutputData heard anything; `.start` is when the first sound came
  after Play and `play.endGap` how long before loopPointReached the last one came (by the filter's
  AudioSettings.dspTime, to a tenth of a second). Also the prepared player's track answers,
  isPlaying of the player and the source at the end, and the sought player's time at seekCompleted.

`residuals.json` records what Unbound does not reproduce, with its cause.

The clip was made with:

    ffmpeg -f lavfi -i testsrc2=size=64x48:rate=10 \
        -f lavfi -i "aevalsrc=0.5*sin(2*PI*(300+150*floor(t*2))*t)|0.25*sin(2*PI*(300+150*floor(t*2))*t):s=48000:c=stereo" \
        -t 3 -c:v libvpx -b:v 1M -c:a vorbis -strict -2 -ac 2 -b:a 192k tone.webm

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/videoaudio/run.py record --label native-a
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/videoaudio/run.py record --label native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/videoaudio/run.py publish native-a native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/videoaudio/run.py gate
```

The probe uses the video probe's scene, runner, packages and settings.

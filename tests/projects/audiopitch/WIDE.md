# Wide output settings

The independent `wide` profile requests the full grid of 32000, 44100, 48000
and 96000 Hz; DSP buffers of 32, 64, 128, 256, 340, 480, 512, 1024, 2048,
4096 and 8192 frames; and Stereo, Quad, Surround, 5.1 and 7.1 output. Its 3080
controls are separate from the original `settings` reference.

| Basis | Contract or distinguishing control |
| --- | --- |
| Configuration | Unity 6000.0 [AudioSettings.Reset](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings.Reset.html) may substitute a supported setting. Each control records the actual configuration and active callback width, rejecting substitutions. The sizes are those listed in the API example; this is a finite measured grid, not a claim about every positive integer. |
| Buffer granularity | [GetDSPBufferSize](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings.GetDSPBufferSize.html) describes block processing. Live edits occur at three distinct render boundaries with audible input in each phase. Each control renders at least 32768 frames, including when the buffer does not divide that length. |
| Generator | [OnAudioFilterRead](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnAudioFilterRead.html) can supply a clipless source and receives interleaved channel data. The generator supplies distinct channel samples, including the last speaker in linked compression. Clip controls supply full-length noise at the actual output sample rate. |
| Effect coverage | Each configuration/layout runs dry output; live Distortion, Chorus, Flange, Normalize, Pitch Shifter and Reverb wet changes; unwrapped Reverb startup; live Send/Receive; Duck release; linked Compressor; source Chorus; listener Reverb; and a fully applied zone. Effect contracts and parameter controls also remain in [SETTINGS.md](SETTINGS.md) and [WET.md](WET.md). |
| Isolation | Each of the 220 configuration/layout cells uses its own process on both backends. Fourteen existing authored mixers are reused as aliases. `WideCases.tsv` is shared by the C# fixture and Python selectors; `wide_cases.py` regenerates it from the original fourteen setting controls. |

The private Editor project starts with both audio buffer settings at 8192 before
its first callback. Starting at 1024 and growing to 2048 previously produced
callback-buffer assertions in the wider layouts; starting at 2048 permitted
independent 96000 Hz / 2048-frame 5.0, 5.1 and 7.1 captures without assertions.
This is a capture setup observation, not a guarantee about runtime Reset recovery.
Authored project settings and the original references remain unchanged.

The full `wide` profile is diagnostic. Its two original independent captures
compare 1,010,860,222 metadata/sample values and disagree at eight values:
wrapped Flange at 32000 Hz / 340 frames in Surround and 5.1, and device-only
source Chorus output at 48000 Hz / 256 frames in 5.1 and 7.1. Current-case Master
floats in the latter controls agree. These failures remain in the complete raw
repeat and engine reports; no original reference or tolerance is changed.

`wide-pcm` keeps the original per-cell startup and control order. It excludes
the complete 32000 Hz / 340-frame and 48000 Hz / 256-frame pairs across all five
layouts: 2940 controls in 210 independent cells. Neither whole pair is added to
Normalize/static-wet admission. Previously supported output contexts remain
available. Every sample and metadata field in each retained cell is compared;
there are no sample masks, frame limits or effect-specific exclusions.

Per-case Reset is a separate diagnostic, not a canonical startup correction.
It removed the two device-only source Chorus disagreements in paired 48000 Hz /
256-frame controls, but the wider diagnostic still disagreed at 32000 Hz / 340
frames and introduced device-only repeat differences at 32000 Hz / 512 frames
in 7.1. It was stopped with partial captures and coverage recorded outside Git.
This does not establish a general reset or retained-history rule. The original
full 3080-control repeats and engine comparisons remain complete and unfiltered.

Record two independent projects with `native.py --release 6000.0.71f1 --rate
48000 --set wide-pcm --work FRESH_CACHE_DIRECTORY`. Verify every GPU renderer
and reject every Editor assertion. Compare with `compare.py --profile wide-pcm
--exact` before publishing with `publish.py --wide-pcm --repeat REPEAT`.
Replay with `context_run.py --release 6000.0.71f1 --profile wide-pcm`.
The 48000 session header is independent of each case's recorded output rate.
Original float tolerances and the one-step device PCM allowance remain unchanged;
Master floats receive no additional allowance. Mono and Prologic outside their
existing profiles, other rates and other buffer lengths remain outside this grid.
Full `wide` captures, including the excluded pairs, remain available with and
without `--reset-cases` for diagnosis.

The separate `configurations` capture records buffer-selection metadata at
48000 Hz, including requests outside the PCM grid. Two independent GPU captures
agree exactly: positive requests from 1 through 384 are retained, zero chooses
1024, and a 16384-frame request chooses 8192. The canonical observations are
[configurations.json](Reference/unity-6000.0.71f1/wide/configurations.json).
Reproduce them with `native.py --release 6000.0.71f1 --rate 48000 --set
configurations --work FRESH_CACHE_DIRECTORY`; compare the two `settings.json`
files exactly. These API observations do not admit effect PCM at the other
buffer lengths. The compatibility profile selects buffer selection before
initial device creation as well as during Reset. Other releases retain their
previous, unmeasured 64-frame minimum in the native backend.

The 32-frame controls distinguish a short DSP period from the 64-frame
connection transition. Wet and dry slopes are recalculated from their current
gains each DSP period; an unfinished transition therefore settles over several
short periods. Unity connections at unity pass directly. The shared connection
implementation handles startup and live edits across all measured layouts;
processing chunks do not define new DSP-period boundaries.

Startup input gain is accumulated once per frame with a single-precision
`1 / bufferFrames` step. At non-power-of-two buffer lengths, dividing the age
by the buffer length gives a different rounded value and can change a later
Q14 delay sample. One precomputed gain curve supplies every group in a mixer;
render chunk boundaries do not restart it. Other releases retain their previous,
unmeasured startup arithmetic until measured separately.

Two independent original-context captures agree exactly on all 964,835,412
values in the 2940-control PCM scope. The engine passes every value under the
existing gate. The strict report retains 85,136 device-only differences;
every Master float passes the original bounds. The complete 3080-control
engine diagnostic retains nine gate failures across the excluded pairs, down
from 85 before the shared ramp corrections. The full original Native repeat
retains eight differences. No excluded diagnostic is treated as an accepted
residual or substituted into the canonical reference.

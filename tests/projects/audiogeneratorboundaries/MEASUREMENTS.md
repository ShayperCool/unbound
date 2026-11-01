# Measured contract for 6000.0.71f1

At 48 kHz, 1024 DSP frames and stereo, the complete independent Native repeats
agree bit for bit across 5,943,389 observations. The canonical reference retains
all 11 cases, all PCM and sink samples, all callback input/output samples and
every recorded lifecycle observation. The callback is `x * 0.5 + 0.0625`.

## One-shot routing

An idle clipless source never invokes the callback. Both group sinks retain
their untouched sentinel and the device output is zero.

`shotFilter` calls only `PlayOneShot`, with no main Play. Its 12 callback input
buffers exactly equal the complete `shotPlain` output. Device PCM, master and
child sinks all equal `shotPlain * 0.5 + 0.0625`, including the existing voice
ramp. After the one-shot finishes, `isPlaying` becomes false and `isVirtual`
true at the next update before block 5. The callback still runs through block
11, producing 0.0625 from zero input. This control does not establish how long
that continuation lasts beyond the measured 12 blocks.

`generatorShot` calls main Play followed by `PlayOneShot`. All 13 generator
callback inputs are zero. Device PCM and both sinks exactly equal the entire
`shotPlain + generator` streams. The one-shot therefore contributes at its
original gain after generation. Overwriting the already mixed one-shot loses
this contribution; applying the callback to their aggregate changes its gain.

## Scheduled windows

Every submission returns without an exception. Times below are sample ticks
relative to the submission DSP clock, with at least 8192 frames of scheduling lead time. Starts use a one-quarter
sample phase and ends a three-quarter sample phase, keeping both absolute
times and durations away from integer sample conversion edges. Scheduling
uses the unrounded DSP clock; each run log retains its absolute clock bits.
Intervals are half-open.

| Case | Requested start | Requested end | Clipless output | Clip-backed voice interval |
| --- | ---: | ---: | --- | --- |
| Start inside block | 8265.25 | 12288.75 | 0.0625 throughout | [8265,12288) |
| End inside block | 8192.25 | 10425.75 | 0.0625 throughout | [8192,10425) |
| Window inside block | 8265.25 | 8377.75 | 0.0625 throughout | [8265,8377) |

The clipless generator ignores both scheduled boundaries in these controls. It
runs from the first captured sample through frame 90111, makes 89 callbacks,
and remains playing and nonvirtual at frame 90112. Both routes stay active.
Explicit Stop then reports not playing and virtual. This control records that
immediate Stop state, without capturing subsequent Stop output.

The three clip-backed controls use the same calls, offsets, callback and
renderer. Their decoded clip input follows the requested window, including the short
interval entirely within one 256-frame block.
The callback runs once per render for all 88 blocks, with zero input outside
the voice window, and continues generating 0.0625. The voice finishes and
reports not playing and virtual; the callback output and routes remain active.
Voice completion therefore does not imply a quiet route in this control.

## Contract for the reviewed change

Preserve processing of a one-shot when a clipless source has no main Play, and
preserve the separate one-shot contribution when a main generator is playing.
Keep the existing generator read-ahead and callback timing. Do not implement
clipless sample-window cropping or scheduled completion from the static warning:
the repeated Native controls measure immediate, persistent generation instead.
These observations are limited to this release and offline renderer layout.

The full replay reports retain every per-channel sample interval, callback
metadata value, missing/extra value and comparison union in the run cache.
The clip-backed witnesses also expose existing filter behavior; their failures
remain visible when they fall outside the reviewed fixes.

The final 955 replay fails 5,166,198 observations. The complete accepted-Root
to 955 union retains 2,738,882 introduced, 2,427,316 retained and 147,463
resolved differences. The one-shot-only filtering failure is introduced; the
generator-plus-one-shot overwrite is retained. Clip-backed witness failures
are retained. No tolerance or observation was removed.

# AudioMixer Duck Volume, Send and Receive probe

The fixture measures the Duck Volume, Send and Receive mixer units of Unity 6000.0.71f1 on Linux
through the production AudioRenderer API. It generates its signals in C# and creates private mixer
assets with the Editor mixer controller API; no recorded audio, external project or default audio
device is used. Every case has its own mixer with auto-suspend disabled: Master with the children
Music, Voice and (when a case names it) Fx. By default Music carries a DC signal (0.25 left,
-0.125 right) through `Attenuation, Duck Volume`, and Voice feeds the duck through a pre-fader Send
at 0 dB while its own fader is at -80 dB, so the sink of Master shows the duck gain directly. Each
case records 32 complete 1024-frame stereo DSP blocks of the master group sink floats
(`<case>.master.f32`) and the returned device PCM (`<case>.f32`). No samples are shifted, trimmed or
discarded. The eleven attenuation-placement controls additionally record the complete listener
mix (`<case>.listener.f32`) before PCM conversion. The device also hears tails from earlier mixers;
the original current-mixer master and device streams are preserved exactly.

The 146 cases (`Cases` in `Assets/Scripts/DuckProbe.cs`) cover:

- Duck Volume: sidechain levels below, in and above the knee; linear ramps of the sidechain level
  for the static curve at Threshold, Ratio (expanding 0.2 and 10), Knee 0/50 and Make-up Gain;
  Attack and Release times (0, 0.01, 0.1 explicit, Editor default, 1 s); Sidechain Mix 0 and 0.5;
  a tone, a left-only and a right-only sidechain; Make-up -20 dB.
- Sends: level -6 dB, the default level (-80 dB), pre- and post-fader placement, two sends into
  one duck, a send without a target group change (`nosend`), a duck in the parent group, and
  group creation orders that put the sender before or after the receiver.
- Receive: sibling and parent receivers in both creation orders, send level with receiver volume,
  a receive in a group that has its own source.
- Bypass of Duck Volume, Send, Receive and their groups (Native ignores all of them).
- Startup: signals inside the first DSP block for a duck, a receive, a sending group and a
  receiving group with its own source.

The Q48 rows add late playback and late AudioSource creation, Send level activation and changes,
running Duck Volume parameter changes via exposed SetFloat/ClearFloat and immediate snapshots,
and routing-unit startup placement controls. Changes occur before rendering blocks 8 and 20;
a late-source control starts at block 3. The complete earlier 51-case references remain bit for bit
unchanged. Q141 adds linear timed snapshots for Send Level and every Duck parameter, weighted
Send/threshold snapshots, half-speed and paused scaled-time controls, and ordinary mixer-output
routing. Q167 adds all five nonlinear transition overrides on Send Level, Duck parameters and ordinary
volumes, dry volume controls, paused BrickwallStart, and weighted-array controls. Single-snapshot
transitions apply overrides; TransitionToSnapshots uses linear interpolation even with a single
element and matching overrides. Volume placement controls cover Attenuation before/after Duck,
Receive, Send, Lowpass, Echo and their bypasses. A trailing Attenuation submits gain directly;
an interior Attenuation ramps per frame, retains its accumulated gain for another changing block,
and holds an unchanged target exactly on the next buffer. Unscaled timed sidechain snapshots and cross-mixer sends remain
explicit failures. The Editor
has no wet-mixing control for Send or Duck Volume; see docs/AUDIO.md for the 6000.0 contracts,
measured rules and follow-ups.
Timed rows also record the exact bits returned by `Time.captureDeltaTime`; the engine receives
that public interval instead of the unquantized `1024/rate` expression.
The Ratio 10-to-0.2 boundary has timed and immediate controls. The timed case also records the
public GetFloat bits at the endpoint, so range acceptance cannot be fixed by changing that value.

## Reproduce

```bash
work="${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/mixer-duck"
for rate in 48000 44100; do
  python3 tests/projects/mixerduck/native.py --release 6000.0.71f1 --rate $rate --work "$work/native-$rate-a"
  python3 tests/projects/mixerduck/native.py --release 6000.0.71f1 --rate $rate --work "$work/native-$rate-b"
  python3 tests/projects/mixerduck/compare.py --release 6000.0.71f1 --rate $rate --exact \
    --reference "$work/native-$rate-a/capture" --candidate "$work/native-$rate-b/capture" \
    --report "$work/agreement-$rate.jsonl.gz"
  # Publish a new reference, or use --extend to add cases while checking all existing sample bits.
  # python3 tests/projects/mixerduck/publish.py --release 6000.0.71f1 --rate $rate --capture "$work/native-$rate-a/capture"
done
python3 tests/projects/mixerduck/run.py --release 6000.0.71f1 --unbound "$UNBOUND"
```

`run.py` is the gate. It assembles the committed Editor-authored project (below), copies `$UNBOUND`
and its `managed/` outputs into a runtime directory, captures the engine at both rates, writes a strict report and
exits with the result of the gate report, which admits one 1/32768 step of device PCM only.
`engine.py` and `compare.py` run single steps; `--before` produces a before/after union. Reports
stream every missing, extra and failing value into gzip JSON lines, followed by a summary that
counts failures per case.

## Engine runs without the Editor

`Authored/unity-6000.0.71f1/default/` holds every file a Native run authored in the Editor: mixer
assets, the scene, `.meta` GUIDs, the Editor's ProjectSettings and `packages-lock.json`.
`authored.json` records the authoring command and the hash of every fixture file it started from.
The set was refreshed for Q167 with `tests/authored_project.py record` from the 48 kHz Native project of a
gate run whose captures passed at both rates; the authoring reads no rate, and the two rates'
projects differ only in random GUIDs. `run.py` assembles it and starts no Editor; `run.py --native`
authors a fresh private Native project per rate in the Editor instead, as before. A set is refused
once any fixture file under Assets/, Packages/ or ProjectSettings/, or `native.py`, has changed:
run `run.py --native` and record its `native-48000/project` again:

```bash
python3 tests/authored_project.py record mixerduck default --release 6000.0.71f1 --project "$work/native-48000/project" \
  --command 'python3 tests/projects/mixerduck/native.py --rate 48000 --release 6000.0.71f1 --work <new cache directory>'
```

The tolerances are ATOL `2e-5` and RTOL `2e-4` on floats, as in the other mixer probes, and exact
on metadata. Independent Native repeats compare float bits exactly and agree at both rates
(19857774/19857774 values each). The canonical reference is one capture per rate in
`Reference/unity-6000.0.71f1/rate-<rate>/`, with gzip sample files. Both raw repeats stay in the
cache.

## Result

Gate: 19857774/19857774 values at 48000 Hz and at 44100 Hz. Independent Editor repeats agree exactly;
every earlier reference value and sample is retained. The strict comparison keeps one-step
device PCM flips whose master floats pass, listed in docs/COMPATIBILITY_GAPS.md. `pcm.py` checks
every device sample against saturating truncation of the pre-quantization float mix, using the listener stream where recorded and the master otherwise; both captures have
zero conversion differences. The measured model and its limits are in docs/AUDIO.md.
The original 84 engine cases also compare bit for bit before and after the timed transition
change: 11010218 values at each rate.

The opt-in `DiagnosticCases` are excluded from the canonical capture. Run one at a time with
`native.py --cases <case>`. The routed controls connect the child's output directly to the receiving group as well as its
Send target. Cross-mixer targets report an invalid audio handle and can prevent
Play mode. Unscaled transitions use wall time even under captureDeltaTime, so their repeats are
not bit-identical. Neither diagnostic establishes a supported DSP rule.

## Deferred snapshot progress roundoff

The shared normalized-progress rule changes the following existing Q167 engine
capture files relative to the preceding implementation. Editor references,
comparators and tolerances remain unchanged. The largest float delta is
2.9802322387695312e-8; device differences are exactly one 1/32768 PCM step.
The existing gate still passes all 19,857,774 values; its strict report retains
918 device PCM failures, as before. These numerical changes are Deferred by the
user's decision; they are not follow-up work. Indices below are zero-based
interleaved stereo sample indices, including all startup frames.

| Engine capture file | Changed values | Maximum absolute delta |
| --- | ---: | ---: |
| `timed_threshold.master.f32` | 2926 | 2.9802322387695312e-08 |
| `timed_threshold.f32` | 2 | 3.0517578125e-05 |
| `timed_knee.master.f32` | 1880 | 2.9802322387695312e-08 |
| `timed_knee.f32` | 2 | 3.0517578125e-05 |
| `timed_threshold_scaled_half.master.f32` | 4272 | 2.9802322387695312e-08 |
| `curve_threshold_smooth.master.f32` | 2776 | 2.9802322387695312e-08 |
| `curve_threshold_smooth.f32` | 1 | 3.0517578125e-05 |
| `curve_threshold_squared.master.f32` | 3734 | 2.9802322387695312e-08 |
| `curve_threshold_squared.f32` | 1 | 3.0517578125e-05 |
| `curve_threshold_sqrt.master.f32` | 2718 | 2.9802322387695312e-08 |
| `curve_knee.master.f32` | 2142 | 2.9802322387695312e-08 |
| `curve_knee.f32` | 4 | 3.0517578125e-05 |
| `curve_dry_smooth.master.f32` | 2048 | 1.4901161193847656e-08 |
| `curve_dry_squared.master.f32` | 4096 | 2.9802322387695312e-08 |
| `curve_dry_sqrt.master.f32` | 2048 | 2.9802322387695312e-08 |
| `control_volume_postduck.master.f32` | 4096 | 1.4901161193847656e-08 |
| `control_volume_postduck.listener.f32` | 2250 | 2.9802322387695312e-08 |
| `control_volume_parent_send.master.f32` | 4096 | 1.4901161193847656e-08 |
| `control_volume_parent_send.listener.f32` | 4096 | 1.4901161193847656e-08 |

| Device file | Sample index | Before | After |
| --- | ---: | ---: | ---: |
| `timed_threshold.f32` | 21896 | 0.1146240234375 | 0.114654541015625 |
| `timed_threshold.f32` | 46194 | 0.204193115234375 | 0.20416259765625 |
| `timed_knee.f32` | 21462 | 0.145477294921875 | 0.1455078125 |
| `timed_knee.f32` | 21463 | -0.072723388671875 | -0.07275390625 |
| `curve_threshold_smooth.f32` | 46630 | 0.211761474609375 | 0.21173095703125 |
| `curve_threshold_squared.f32` | 45746 | 0.177154541015625 | 0.1771240234375 |
| `curve_knee.f32` | 21676 | 0.14154052734375 | 0.141571044921875 |
| `curve_knee.f32` | 45310 | 0.207489013671875 | 0.20745849609375 |
| `curve_knee.f32` | 46366 | 0.20928955078125 | 0.209259033203125 |
| `curve_knee.f32` | 46367 | -0.104644775390625 | -0.1046142578125 |

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
discarded.

The 100 cases (`Cases` in `Assets/Scripts/DuckProbe.cs`) cover:

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
routing. Unscaled timed sidechain snapshots, non-linear transition overrides and cross-mixer sends
remain explicit failures. The Editor
has no wet-mixing control for Send or Duck Volume; see docs/AUDIO.md for the 6000.0 contracts,
measured rules and follow-ups.
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
The set was recorded with `tests/authored_project.py record` from the 48 kHz Native project of a
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
(13107403/13107403 values each). The canonical reference is one capture per rate in
`Reference/unity-6000.0.71f1/rate-<rate>/`, with gzip sample files. Both raw repeats stay in the
cache.

## Result

Gate: 13107403/13107403 values at 48000 Hz and at 44100 Hz. Independent Editor repeats agree exactly;
every earlier reference value and sample is retained. The strict comparison keeps one-step
device PCM flips whose master floats pass, listed in docs/COMPATIBILITY_GAPS.md. `pcm.py` checks
every device sample against saturating truncation of its own master sample; both captures have
zero conversion differences. The measured model and its limits are in docs/AUDIO.md.
The original 84 engine cases also compare bit for bit before and after the timed transition
change: 11010218 values at each rate.

The opt-in `DiagnosticCases` are excluded from the canonical capture. Run one at a time with
`native.py --cases <case>`. Cross-mixer targets report an invalid audio handle and can prevent
Play mode. Unscaled transitions use wall time even under captureDeltaTime, so their repeats are
not bit-identical. Neither diagnostic establishes a supported DSP rule.

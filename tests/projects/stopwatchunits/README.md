# Stopwatch tick unit probe

The unit of `System.Diagnostics.Stopwatch`'s ticks as game code sees it in the Editor's Play mode
on Unity 6000.0.71f1 (Mono 6.13). `StopwatchProbe` records `Stopwatch.Frequency`,
`IsHighResolution`, whether `ElapsedTicks` equals `Elapsed.Ticks`, and over a 200 ms sleep the
ratios of `ElapsedTicks` and of the `GetTimestamp()` difference to `Elapsed.Ticks` (to one
decimal) and the sleep in seconds from each measure (to a tenth), also as game code converts
ticks (divided by 10 000 000). The probe reads `Frequency` both as a value and in place
(`Stopwatch.Frequency.ToString()`, which C# compiles to a load of the field's address).

Record two independent Editor captures (GPU, existing display), publish after they agree, run
Unbound and compare (modelroot's comparator):

```bash
python3 tests/projects/stopwatchunits/run.py editor --release 6000.0.71f1 --label <label>
python3 tests/projects/stopwatchunits/run.py publish --release 6000.0.71f1 --label <label>
python3 tests/projects/stopwatchunits/run.py unbound --release 6000.0.71f1
```

The Linux Editor pair records: `Frequency` is 10 000 000 and
`IsHighResolution` true; `ElapsedTicks` equals `Elapsed.Ticks` and timestamps count the same
100 ns ticks. .NET counts nanoseconds on macOS and Linux (`Frequency` 1 000 000 000), so game code
that turns raw ticks into seconds by `TimeSpan.TicksPerSecond` ran 100 times too fast in Unbound.
Unbound redirects game assemblies' `Stopwatch.Frequency`, `GetTimestamp()` and `ElapsedTicks` to
Mono's unit when they load; engine code keeps .NET's.

The Scripting API documents none of this (the class is .NET's); The canonical capture establishes the unit on this Linux release only. Not covered: `Stopwatch` members .NET added after the Unity
profile (`GetElapsedTime`), delegates made of these members, reflection.

The published `Reference/unity-6000.0.71f1.jsonl` has 12 records from independent
windowed NVIDIA GPU captures, both normal exit 0 and byte-identical. The runner
uses the existing display and starts no X server. Before the tick-unit correction,
the original 10-row probe matched 5/10 records: Frequency, ElapsedTicks equality, both tick ratios and
the conversion through TimeSpan's unit differed. The corrected runtime matches
12/12 after the added field-reference control. This reference measures Linux; it does not supply a macOS capture.

The [Unity 6000.0 .NET library documentation](https://docs.unity3d.com/6000.0/Documentation/Manual/dotnetSystemLibraries.html)
describes the class-library context. The .NET [ElapsedTicks contract](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.stopwatch.elapsedticks)
relates a tick to `1 / Frequency`; it does not prescribe 100 ns. That exact unit is
an observation of this Editor release. The comparator and its tolerance are unchanged.

The escaping-reference control returns `ref readonly long` to `Stopwatch.Frequency`
from a method that cannot be inlined, then uses stack space before reading it.
`stackControlSum` confirms that the stack-using call ran; `frequencyFromEscapedReference`
must still read 10 000 000. Both are recorded in the Editor. Field loads and addresses
are redirected to one static readonly shim field, so its lifetime matches the original
field. The previous local-address rewrite failed this control.

Against the extended 12-row reference, the original runtime matches 6/12 and the
corrected runtime 12/12. The original ten reference rows are unchanged; only
`stackControlSum` and `frequencyFromEscapedReference` were added.

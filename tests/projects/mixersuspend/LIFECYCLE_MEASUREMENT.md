# Suspension history after parameter changes and quiet siblings

This separate experiment is defined before capture. It contains no reset during
recording and does not replace or narrow either unresolved reset experiment.
Every case retains 1024 complete stereo DSP blocks at each claimed output rate.

An ended source seeds a five-second Echo. In the control, a looping sibling starts
at block 384, keeping the mixer awake long enough to observe the preserved delay.
The gain, wet and zero-duration snapshot cases change one parameter at block 192,
then use the same later sibling wake. The resumed delayed copy's position tests
whether the parameter change advanced history during the silent gap. No private
suspension state is read. Mono input has a separate gain/wake case.

The muted-sibling and silent-sibling controls start a looping sibling before the
first render. The muted sibling has a serialized group mute; the silent sibling
plays a zero-filled clip. An independent unmuted Echo group makes their effect on
whole-mixer suspension audible. Playing and virtual flags are recorded at blocks
32 and 400. Raw sinks, device PCM and every listener block, including all zeros,
are retained without alignment or trimming.

Each case explicitly requests the public default Snapshot with zero duration and
clears exposed overrides before its first yield. This defines the initial snapshot
through public calls rather than depending on the Editor's selected target snapshot.
It is a distinct measurement from the original fixture's initial snapshot case;
that original canonical reference is unchanged.

Two independently agreeing complete Native captures per rate are required before
acceptance or publishing a lifecycle profile. Metadata and Native repeat bits are
exact. Engine tolerances remain ATOL 2e-5 / RTOL 2e-4.

Independent Native pairs agree exactly at both rates. Gain, wet and snapshot
changes retain the control's resumed history position; they do not advance it
during this measured gap. A muted sibling does not keep the mixer awake, although
it remains playing and real. A silent clip on an unmuted sibling does keep the
mixer awake and preserves the unsuspended five-second repeat. Inactive sources
report virtual at the measured observation points.

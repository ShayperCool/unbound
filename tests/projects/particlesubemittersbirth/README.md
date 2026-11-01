# Birth subemitter clocks

The release reference retains the original parent/child histories and the attached,
detached, and `withChildren` controls. The `phase_*` family adds one event-time
discriminator with irregular deltas. It moves a single parent's burst from zero to
`.017`, changes the child rate from 13 to 17, adds child delay `.023`, and compares
rate emission with a child burst at `.031`. Every child inherits nonwhite color and
size. All particles outlive these short captures.

Prepare each independent capture with `run.py prepare`, then run `run.py native`
or `run.py engine` with the same explicit `--unity-version`, cache `--work`, and
unique `--label`. Engine captures also require `--cli` and `--runtime`. The runner
wraps each actual capture with the installed heavy-job scheduler; do not wrap it
again. It creates private projects and keeps authored inputs and output manifests.

`verify.py --unity-version VERSION --native-a A --native-b B --output REPORT`
checks repeat agreement, the single canonical release reference, and the original
broad birth records. With `--actual CAPTURE`, it emits all missing, extra, and
failing values at the broad comparator's unchanged tolerances. Full engine parity
is still red.

`diagnose.py --unity-version VERSION --actual CAPTURE --previous-reference REF94
--previous-actual CAPTURE94 --output REPORT` additionally checks exact preservation
of previous histories, emits the complete previous failure union, and reconstructs
ballistic origins from every new child particle. Supply the prior published
reference and its paired engine capture explicitly. The report preserves both
the observed child age and the elapsed time since the scheduled parent event.
Their difference describes the unresolved newborn chronology; it is not a
correction to apply to particle age.

The Native origins follow the parent's position at child emission age, including
child delay, across these irregular steps. The child ages do not consistently
equal elapsed time since that event. Neither a constant age offset nor a
`fixedDeltaTime` explanation follows from this witness. Changes to shared emission
or simulation require coordination with their owners.

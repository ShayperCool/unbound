# Instantiated mixer groups and snapshots

Unity 6000.0.71f1, recorded twice with the macOS Editor in batch mode; the two captures are
byte-identical and the canonical reference has 33 rows. The values are managed object state, so
they do not depend on the OS or the graphics device. The gate compares every row exactly.

The probe loads a mixer (two groups, Master and Music, and one snapshot) from `Resources` in Play
mode and instantiates each group and the snapshot:

- AudioMixerGroup and AudioMixerSnapshot declare `UnityEngine.Internal.ISubAssetNotDuplicatable`
  (`*.interfaces`).
- A clone is made (`*.result`; the Editor's own classes are its controller subclasses, recorded as
  the public type), named `(Clone)`, with a new instance id, no hide flags (`*.name`, `*.same`,
  `*.newId`, `*.hideFlags`).
- It keeps its mixer (`*.mixer`, `*.mixerSame`) but is not one of the mixer's groups
  (`*.inMixer`) or snapshots (`snapshot.found`); a cloned snapshot's `TransitionTo` raises no error
  (`snapshot.transition`).
- An AudioSource cloned with its GameObject keeps the original group (`source.*`).

Not recorded: how a source routed to an instantiated group sounds, and `Instantiate` of the
AudioMixer itself (unsupported in Unbound; it throws). Other releases are unmeasured.

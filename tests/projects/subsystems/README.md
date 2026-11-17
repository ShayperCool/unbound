# Subsystems without providers

`Assets/Scripts/SubsystemsProbe.cs` writes values.jsonl in `$SUBSYSTEMS_OUT`
(tests/value_probe.py): for every action, what it returns or throws, the subsystem's and the
provider's callbacks it runs, in order (`.calls`), and the log messages it writes (`.logs`).

* `start.*`: the descriptors and subsystems Play mode starts with: none, native (integrated) or
  managed, with no subsystem provider plug-in in the project.
* `register.*`: `SubsystemDescriptorStore.RegisterDescriptor` of a descriptor with a provider: the
  descriptors listed after it, the same descriptor and another with its id again, null and empty
  ids, a missing provider type, a provider or subsystem type of the wrong class, and null.
* `create*`, `run*`, `stop*`, `destroy*`, `recreate*`: creating the descriptor's subsystem (and
  again, also through the interface), starting, stopping and destroying it (each twice), starting
  it after it is destroyed and creating it again; the subsystems the manager lists after each.
* `fail.*`, `derived.*`, `proxy.*`: a provider that does not initialize, a descriptor whose
  subsystem type is overridden, and a proxy of a provider without its subsystem.
* `end.*`: the subsystems left, and whether the reload events fired.

Measured in the Unity 6000.0.71f1 Editor on Linux with an NVIDIA GPU, windowed (two
byte-identical captures). Not measured: the deprecated `Subsystem`/`SubsystemDescriptor` API (no
public registration in this release) and integrated subsystems (they need a native provider).
`run.py record` / `publish` / `gate` (tests/value_probe.py).

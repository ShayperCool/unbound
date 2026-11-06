# Seeded particle random values

Unity release: 6000.0.71f1. Run with `UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1`.
`run.py native --label <unique-label>` records a private project copy through the Editor job pool.
Two identical captures are required by `run.py publish <label1> <label2>`.
`run.py check` runs only Unbound and compares every recorded value against the committed reference.
Outputs and repeat captures stay in the validation cache.

The probe covers fixed seeds, start values, shapes, module random modes, burst counts and
probabilities, Birth sub-emitters, custom data, mesh selection, Simulate and Play. Seeds are stored
as two exact 16-bit halves. The comparator requires exact halves, counts and mesh indices.
Other values use the gravity probe's existing absolute and relative tolerance of 0.0002.

The Unity 6000.0 [Main module](https://docs.unity3d.com/6000.0/Documentation/Manual/PartSysMainModule.html)
promises repeatable playback when automatic seeding is disabled.
[ParticleSystem.randomSeed](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem-randomSeed.html)
selects that effect, and
[Particle.randomSeed](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.Particle-randomSeed.html)
keeps each particle's random properties stable during simulation.
[MinMaxCurve](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.MinMaxCurve.html)
describes the constant/curve interpolation modes. These pages do not specify a generator,
module salts or draw order. Exact numeric sequences here are measurements of this release.

Recovered authored sources and the initial reference from branch `t3/particle-random-stream`
at base `d370c642`. No unlanded commits or tracked edits existed there. The independent cached
captures `a1` and `a2` agree exactly. The recovered baseline matched 70 of 277 records.

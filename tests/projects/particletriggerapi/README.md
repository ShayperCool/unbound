# particletriggerapi

What `GetTriggerParticles` and `SetTriggerParticles` do in Unity 6000.0.71f1, for every
ParticleSystemTriggerEventType, and what `TriggerSubEmitter` emits from dead particles that carry
fading trails. Played frames at `Time.captureDeltaTime` 0.02. Each trigger scenario plays a
world-space system without emission of its own; six particles are emitted along +X at 1.5 m/s
(one at 0.75 m/s) from x -0.5 at heights 0, 0.1, 0.45, 0.7 and 2, through a 1 m trigger box A at
x 1, a trigger sphere B (radius 0.4) at x 1.3 overlapping it, and a box C at x 3. All four events
are Callback. `OnParticleTrigger` (`TriggerApiCallback`) records per event type the safe size, both
`GetTriggerParticles` overloads, each particle, its `ColliderData` colliders and what reading past
the last one does; by scenario it writes particles back. The probe records the buffer every few
frames.

- `read/{disabled,one,all}`: the three collider query modes, reading only.
- `write/all`: whole-list write-backs (Enter recoloured, Inside velocities halved, Exit resized).
- `write/subset`: `SetTriggerParticles(Inside, list, 1, 2)` after enlarging every Inside particle.
- `write/kill`: Enter particles written back with zero remaining lifetime.
- `write/twice`: two write-backs of the Inside list in one callback.
- `update`: Get and Set from `Update`, outside the callback.
- `exitkill`: Exit is Kill.
- `carrier/*`: a parent whose particles die at 0.3 s leaving their trails to fade, then
  `TriggerSubEmitter` on every particle, on a `GetParticles` list, on one carrier and with
  InheritLifetime.

Each scenario's system is destroyed and the next starts a frame later (`*/gone`).

    python3 tests/projects/particletriggerapi/run.py native --label native1
    python3 tests/projects/particletriggerapi/run.py publish native1 native2
    python3 tests/projects/particletriggerapi/run.py check          # the gate

The reference is `Reference/unity-6000.0.71f1.jsonl` (5299 records, two byte-identical GPU Editor
captures). The comparator is `particletrails/compare.py` with `known.json`.

## Documented contract (Unity 6000.0)

| Source | What it says |
| --- | --- |
| [ParticlePhysicsExtensions.GetTriggerParticles](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticlePhysicsExtensions.GetTriggerParticles.html) | Gets the particles that met the trigger event's condition; the ColliderData overload also says which colliders. |
| [ParticlePhysicsExtensions.SetTriggerParticles](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticlePhysicsExtensions.SetTriggerParticles.html) | "Write modified particles back to the Particle System, during a call to OnParticleTrigger." offset: "The offset into the destination particle array, if only setting a subset of particles"; count: "The number of particles to set". |
| [ParticleSystem.TriggerModule.colliderQueryMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.TriggerModule-colliderQueryMode.html), [ParticleSystemColliderQueryMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystemColliderQueryMode.html) | Disabled, One ("the first collider"), All. |
| [ParticleSystem.TriggerSubEmitter](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.TriggerSubEmitter.html) | Triggers the sub-emitter on all particles or on the given ones. |
| [Object.Destroy](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Object.Destroy.html) | "Actual object destruction is always delayed until after the current Update loop, but is always done before rendering." |

No list order is documented; none is promised here.

## Measured behaviour

| Records | 6000.0.71f1 |
| --- | --- |
| `read/*` | ColliderData lists, per particle, no collider (Disabled, with the warning "Trigger module must enable collider queries in order to retrieve which colliders caused each trigger event." on every call), the first in the module's list (One) or all of them in that order (All); reading past the last throws. The overload throws InvalidOperationException for Outside and Exit. |
| `read/all` frames 68-69 | A particle that leaves a box during the step is still inside in that step: the step's movement is swept. Moving from A into the overlapping B is no Exit and Enter. |
| `write/all` | A list read after another list's write-back shows the written particles: the lists are views of the live particles. |
| `write/subset` | The list's first `count` particles go to the entries from `offset` on, whole, seed included; the call throws ArgumentOutOfRangeException ("offset+count should be smaller than the size of the particles list.") unless offset + count is smaller than the list's size (1 + 2 of 3 throws, of 4 writes). An entry whose particle was replaced by another one (another seed) is no longer returned; the safe size still counts it. |
| `update` | Outside the callback the lists are empty, and SetTriggerParticles writes nothing and logs nothing. |
| `carrier/*` | TriggerSubEmitter emits from carriers as from living particles; with InheritLifetime their children start at zero life and are gone at the child's next update. |
| `*/gone` | A system destroyed in a coroutine during Update does not update or send OnParticleTrigger that frame. |

## Known differences (`known.json`)

- `outside-order-after-exit-kill` (3 records): after an Exit Kill compacts the buffer, the Outside
  list holds the same particles with the same values in another order.

Unbound's choices (unmeasured): a write-back skips entries whose particle is gone or was killed by
a trigger in this update.

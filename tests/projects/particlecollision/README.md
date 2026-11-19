# Particle collision events and force-field vector fields

Unity 6000.0.71f1, Linux GPU Editor (OpenGL Core), Play mode (domain reload disabled) at
Time.captureFramerate 50. Everything is made by `CollisionProbe` at run time. Two windowed captures
were byte-identical.

- `collide/*`: systems shooting down with World collision (Collision3D) at a static BoxCollider, a
  BoxCollider with a kinematic Rigidbody, a BoxCollider child of a kinematic Rigidbody, two boxes
  hit by one system, a BoxCollider2D (Collision2D) and a collision plane. `HitRecorder` records who
  sent each object OnParticleCollision and, on the first call, GetCollisionEvents for the object,
  GetSafeCollisionEventSize, the count a one-element array takes and the first event's
  colliderComponent.
- `field/*`: four particles (inside a Box force field, one outside) emitted at rest plus 0.5 along
  x, stepped by ParticleSystem.Simulate, in a field whose vector field is a 4x4x4 RGBAFloat
  Texture3D made at run time, speed 3, attraction 0, 0.5, 1 and 2 at 0.05 s steps, and at 0.02 s
  steps attraction 1, -1, a particle emitted at the third step, the field turned 90 degrees about y
  and the External Forces multiplier 0.5; one field used by a system, then a fresh second system
  in it; a system playing with rate emission that starts empty.

| Source (6000.0) | Documented | Measured here |
|---|---|---|
| [ParticleCollisionEvent.colliderComponent](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleCollisionEvent-colliderComponent.html) | The Collider or Collider2D the particle hit. | The collider hit; a plane's events name the plane's Transform. A collider under a Rigidbody reports to the Rigidbody's GameObject (OnParticleCollision there, GetCollisionEvents for it), the event still naming the collider. |
| [ParticlePhysicsExtensions.GetSafeCollisionEventSize](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticlePhysicsExtensions.GetSafeCollisionEventSize.html) | A safe array size for GetCollisionEvents. | The system's events of the update over all objects (12 with 11 and 1 on two boxes); an array takes as many as it holds. |
| [ParticleSystemForceField.vectorField](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystemForceField-vectorField.html), vectorFieldSpeed, vectorFieldAttraction | The texture, its speed, how strongly particles are dragged toward it. | Each step the velocity relaxes toward the field's nearest texel at the particle (over the field's range) times the speed by attraction × multiplier × 30 × dt, unclamped (attraction 2 overshoots), the texel turned with the field; no effect with a zero or negative attraction, in the system's first step, whether or not it had particles (a particle emitted later is moved in its first step; a fresh system in a field another system already used is not) or outside the field. |

Unbound: 212/214 with 2 recorded residuals (`residuals.json`: 2D world collision, unsupported).
Unbound applies the vector field in the measured context only (a Box field without scale, with
constant speed and attraction, without drag or rotation) and reports the others as unsupported;
a restarted system starts over (inferred). Unmeasured: those cases, imported vector field assets.

```sh
python3 tests/projects/particlecollision/run.py editor --release 6000.0.71f1 --label L
python3 tests/projects/particlecollision/run.py publish --release 6000.0.71f1 --label L
UNBOUND_CLI=/path/to/runtime/unbound python3 tests/projects/particlecollision/run.py check --release 6000.0.71f1
```

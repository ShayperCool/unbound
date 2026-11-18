# Particle jobs: IJobParticleSystem* and OnParticleUpdateJobScheduled

Unity 6000.0.71f1, recorded with the macOS Editor in batch mode in Play mode (two identical
captures). None of the values depends on the OS or the graphics device. The gate compares every
value exactly.

`Assets/Scripts/ParticleJobsProbe.cs` makes systems with no emission and no shape (Always Simulate,
local space) and emits particles with known values into them:

- `A` (custom data module on, Custom1 a 4-component constant vector), with two scripts receiving
  OnParticleUpdateJobScheduled, a disabled one, and one declaring the message with a
  `ParticleSystemJobData` parameter (as UniTask's trigger does);
- `Other` (one particle), `Empty` (playing, no particles), `Stopped` (stopped with clearing when it
  is made, after Play On Awake played it), `Paused` (one particle, paused), `Simulated` (one particle,
  never played, later Simulate), `StopClearLater`/`StopLater` (playing, empty, stopped with and
  without clearing in a later frame), `NeverPlayed` (made inactive with Play On Awake off).

Frame by frame it schedules, from A's message: a job copying what it sees of three particles; a job
writing every component; two chained jobs; a parallel-for job (`Schedule(ps, 1)`) and a batch job
(`ScheduleBatch(ps, 2)`) after it; a job setting `aliveTimePercent` to 100 and 150. It also schedules
each kind from Update, and from Other's message a job on A.

Measured:

- Scheduling outside the message throws InvalidOperationException ("Particle System jobs can only be
  scheduled in MonoBehaviour.OnParticleUpdateJobScheduled()"); inside any system's message a job on
  another system may be scheduled.
- The message comes after Update and before LateUpdate, to every script of the object, disabled
  ones included, in component order. A system gets it in every frame its update steps it (playing,
  also when empty; stopped with particles), and in the frame a script stops it; not when paused,
  not after its stopping frame, not when it never played. Simulate sends it to the system it steps.
- A method with a parameter is not called; the Editor logs "Script error:
  OnParticleUpdateJobScheduled / The message may not have any parameters. / The message will be
  ignored."
- A job sees positions, velocities (without animated velocity), axes of rotation, rotations and
  rotational speeds in radians, 3D start sizes, start colors, the percentage of the lifetime lived,
  the inverse start lifetime, random seeds and Custom1 as the module made it; Custom2 (off) and the
  mesh indices (billboards) are arrays that were not created, of the particle count.
- Writes reach the particles: rotations read back in degrees, the lifetime percentage and inverse
  start lifetime give the remaining and start lifetimes (150 % gives -2 s remaining; 100 % and 150 %
  die at the next update); custom data holds what the job wrote until the module's next run.
- A later job on the system runs after the earlier one; parallel jobs run each index once, batches
  are (0, 2) and (2, 1).
- A read of the system before its job has run sees the particles as they were (the job is still in
  its batch); once the job has run, a read sees its writes before the update ends.

Unmeasured: Mesh-mode mesh indices, jobs scheduled from a Simulate call's message (Unbound completes
them before the call returns), sub-emitters, world simulation space, other releases.

# Particle jobs: IJobParticleSystem* and OnParticleUpdateJobScheduled

Unity 6000.0.71f1, recorded with the macOS Editor in batch mode in Play mode (two identical
captures). None of the values depends on the OS or the graphics device. The gate compares every
value exactly.

`Assets/Scripts/ParticleJobsProbe.cs` makes systems with no emission and no shape (Always Simulate,
local space) and emits particles with known values into them:

- `A` (custom data module on, Custom1 a 4-component constant vector), with two scripts receiving
  OnParticleUpdateJobScheduled, a disabled one, and one declaring the message with a
  `ParticleSystemJobData` parameter (as UniTask's trigger does);
- `NoAxis` (plain particles), `MeshAlloc` (AllocateMeshIndexAttribute, billboards), `Pending` (a job
  writing velocities, then SetParticles of new positions in LateUpdate);
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
  the inverse start lifetime, random seeds and Custom1 as the module made it; Custom2 (off), the
  mesh indices (billboards) and the axes of rotation of particles given none are arrays that were not
  created, of the particle count. AllocateMeshIndexAttribute gives billboards' jobs mesh indices
  (-1), and a job's write is there in the next frame's job.
- Writes reach the particles: rotations read back in degrees, the lifetime percentage and inverse
  start lifetime give the remaining and start lifetimes (150 % gives -2 s remaining; 100 % and 150 %
  die at the next update); custom data holds what the job wrote until the module's next run.
- A later job on the system runs after the earlier one; parallel jobs run each index once, batches
  are (0, 2) and (2, 1).
- A read in the message after scheduling sees the particles as they were. The job runs once the
  messages are over, alongside the frame: a read after it has run (waited for in LateUpdate) sees its
  writes before the update ends. Whether a LateUpdate read without waiting comes before or after the
  job depends on timing, so it is not recorded. SetParticles in LateUpdate keeps the job's velocities
  with the script's positions.

Unmeasured: Mesh-mode mesh indices, a script's write in the message while its job has not run, jobs
scheduled from a Simulate call's message (Unbound completes them before the call returns),
sub-emitters, world simulation space, other releases. Unbound finishes the messages' jobs right after
the messages, so its scripts always see a job's writes after the update.

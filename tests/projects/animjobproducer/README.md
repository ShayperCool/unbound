# Animation job producer

Unity 6000.0.71f1, recorded twice with the macOS Editor in batch mode; the two captures are
byte-identical and the canonical reference has 12 rows (managed reflection and a manually
evaluated playable graph; independent of the OS and the graphics device). The gate compares every
row exactly:

```sh
python3 tests/projects/animjobproducer/run.py check
```

- `marker`, `marker.onJobStruct`, `producer`: IAnimationJob's JobProducerType names the internal
  struct ProcessAnimationJobStruct`1 (public static Execute and GetJobReflectionData); a job struct
  inherits no marker.
- `reflectionData*`: GetJobReflectionData returns data, the same for a job type each time and
  different per job type.
- `execute.*`: from inside a running job's ProcessAnimation, Execute on another job with method
  index 1 runs its ProcessAnimation and 0 its ProcessRootMotion, both on the stream copied from the
  pointer (valid, the running job's delta time), changing the job passed by reference; index 2
  throws NotImplementedException.
- `evaluate.*`: each graph evaluation calls the job's ProcessRootMotion, then its ProcessAnimation,
  also with root motion applied, and the job keeps its changes.

Other releases are unmeasured.

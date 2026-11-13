# TextAsset memory accounting

Two independent Unity 6000.0.71f1 Editor runs on the NVIDIA GPU agree on all
40 reference values. String constructors cover ASCII and multibyte UTF-8 data
on both sides of the 31-byte boundary. Binary constructors include embedded zero
bytes. Clones retain the same accounting after the original is destroyed, and
long/short names and text/bytes access leave the measured result unchanged.

The measured rule is a 160-byte object plus no additional payload allocation
through 31 bytes. Larger payloads add their byte length and one trailing byte.
The rule uses byte length, including for text. Managed tests also check lengths
37 and 32768 outside the captured matrix. The query uses dataSize, so it does not
copy the bytes or force a lazy imported file into memory.

The [6000.0 Profiler documentation](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Profiling.Profiler.GetRuntimeMemorySizeLong.html)
describes object memory accounting; it does not guarantee these byte constants.
They are observations from this release, selected through Compat. Other releases
keep the previous unsupported fallback. Imported files, AssetBundles and unloading
are not independently captured by this fixture.

```sh
python3 tests/projects/textassetmemory/run.py editor --release 6000.0.71f1 --label fresh
python3 tests/projects/textassetmemory/run.py publish --release 6000.0.71f1 --label fresh
UNBOUND_CLI=/immutable/runtime/unbound python3 tests/projects/textassetmemory/run.py check --release 6000.0.71f1
```

The gate checks every value exactly. The broader allocation questions remain in
[appfacts/MemoryControl](../appfacts/MemoryControl/README.md).

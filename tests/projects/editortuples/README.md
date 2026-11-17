# Editor tuples in Play mode

The 72-row Unity 6000.0.71f1 reference records tuple factories of arity one through eight,
item formatting, structural equality and comparison, exact integer hashes, custom
comparers, indexing, null values and nested rest tuples through 30 items. Two independent
windowed GPU Editor captures agreed and exited normally on the NVIDIA GeForce RTX 4060. The controls reflect the
internal tuple APIs, which are absent from the release's public
[Scripting API index](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UnityEditor.html).
These results are measured release behavior, rather than a documented Unity guarantee.

Run from the repository root:

```sh
python3 tests/projects/editortuples/run.py editor --release 6000.0.71f1 --label native
python3 tests/projects/editortuples/run.py publish --release 6000.0.71f1 --label native
python3 tests/projects/editortuples/run.py unbound --release 6000.0.71f1 --label engine
```

The shared runner stages authored files into the validation cache, checks completion,
GPU use and repeat equality, and retains every missing, extra or differing row in the
comparison report. Engine captures use an immutable runtime snapshot. Argument exceptions
record their whole message (in the Editor's layout, the parameter name on its own line) and
their parameter name.
The measured resource-key error messages apply to 6000.0.71f1; other releases retain their
previous descriptive messages until measured. Hash grouping and tail selection apply to
the general tuple structure, with custom comparers exercised separately from default hashes.

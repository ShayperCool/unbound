# Handles state and line geometry

This probe measures Unity 6000.0.71f1 Editor state for `Handles.zTest` and `Handles.lighting`.
It checks defaults, assignment and nested drawing scopes. Changing those properties inside
a drawing scope leaves the new values in place when the scope restores color and matrix.
Both property accessors reject worker-thread calls.

The line helpers cover signed and unclamped parameters, clamped segment projection, coincident
endpoints and a short nonzero segment. The distance calculation also runs on a worker. Handle
drawing and picking are separate work.

```sh
python3 tests/projects/handlesstate/run.py record handles-native-a
python3 tests/projects/handlesstate/run.py record handles-native-b
python3 tests/projects/handlesstate/run.py publish handles-native-a handles-native-b
UNBOUND=/path/to/unbound python3 tests/projects/handlesstate/run.py gate handles-engine
```

# ObjectIdRequest in Gamma and Linear projects

The Unity 6000.0.71f1 Play mode capture (windowed Linux GPU Editor, URP project of
`tests/projects/graphicsgaps`, two identical captures per colour space) records what
`Camera.SubmitRenderRequest` with an `ObjectIdRequest` writes into an ARGB32 target created sRGB and
one created linear: the pixels of four cubes (ids 1 to 4, far to near) and the background, their
decoded ids and the target's colour histogram. In a Linear project the sRGB target encodes the ids
(1, 2, 3, 4 read 13, 22, 28, 34); the linear target and every target of a Gamma project keep them.

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/objectidspace/run.py record --space Linear --label linear-a
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/objectidspace/run.py publish --space Linear linear-a linear-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/objectidspace/run.py gate
```

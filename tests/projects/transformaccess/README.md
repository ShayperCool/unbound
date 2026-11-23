# TransformAccessArray capacity and length

`Assets/Editor/TransformAccessProbe.cs` records in Play mode, for arrays of up to six new
transforms: a capacity set below, at and above the length (also after a growth, and negative),
then `SetTransforms`, `Add` and `RemoveAtSwapBack`; what the array then holds (length, capacity,
elements, an index past the end) and what an `IJobParallelForTransform` over it reaches; the
log messages of the call and of the rest of the step; the capacities `Add` grows to. Values
compare exactly. (A capacity of `int.MinValue` crashes the Editor; it is not recorded.)

```sh
python3 tests/projects/transformaccess/run.py record --label A     # twice, A and B
python3 tests/projects/transformaccess/run.py publish A B
python3 tests/projects/transformaccess/run.py gate
```

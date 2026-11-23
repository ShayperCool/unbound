# The search picker's object field

`Assets/Editor/SearchObjectFieldProbe.cs` records in Play mode, outside any panel, what
`UnityEditor.Search.ObjectField` (a UI Toolkit field) is: the classes and child tree its two
constructors build, its defaults (`objectType`, `searchViewFlags`, `searchContext`, `value`,
`singleLineHeight`, which elements take focus), what its display shows for no value of several
object types and for a GameObject and a texture, how a GameObject is read for a component
`objectType` (also one the GameObject does not have), the change events it sends without a panel,
and its UXML data (`CreateInstance`, and `Deserialize` with and without its `type` attribute).
Values compare exactly. The search picker window (`ShowObjectSelector`) and the IMGUI
`DoObjectField` need a window and are not recorded.

```sh
python3 tests/projects/searchobjectfield/run.py record --label A     # twice, A and B
python3 tests/projects/searchobjectfield/run.py publish A B
python3 tests/projects/searchobjectfield/run.py gate
```

# PopupWindowContent lifecycle

The Unity 6000.0.71f1 Play mode capture records the default content size and hooks, window
ownership before and during `OnOpen`, `CreateGUI` and `OnClose`, and `PopupWindow.Show` both
inside and outside `OnGUI`. The retained-content control verifies parent attachment and records
the automatically calculated window size as an open difference.

The Scripting API documents the content hooks and identifies `editorWindow` as the window that
hosts the content. The probe checks the same window reference at open and close. It also checks
that requesting another popup of the same content type closes the current one, that an immediate
request from the same anchor is suppressed, and that the anchor can reopen after the delay.
[Unity 6000.0 `PopupWindowContent`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/PopupWindowContent.html)
[Unity 6000.0 `PopupWindow.Show`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/PopupWindow.Show.html)

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/popupcontent/run.py record --label native-a
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/popupcontent/run.py record --label native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/popupcontent/run.py publish native-a native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/popupcontent/run.py gate
```

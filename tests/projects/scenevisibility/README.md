# scenevisibility

What `SceneVisibilityManager` answers and raises in Unity 6000.0.71f1 Play mode
(`Assets/Scripts/SceneVisibilityProbe.cs`; two byte-identical macOS batch Editor captures; no value
depends on the OS or the graphics device). On a hierarchy built in the empty probe scene (A with A1
and A2, A2 with A2a; B) the probe runs one operation per Editor update: hiding and showing objects
with and without descendants, arrays, toggling, the scene's and every scene's Hide/Show, isolation
(and showing an object while isolated), the same for picking, null arguments, an object added under
a hidden parent and a hidden object's child destroyed. Each row holds the exception, the
visibilityChanged/pickingChanged events raised during the call and before the next update, then
each object's IsHidden, IsHidden with descendants, AreAllDescendantsHidden,
AreAllDescendantsVisible, IsPickingDisabled, IsPickingDisabled with descendants,
IsPickingDisabledOnAllDescendants and IsPickingEnabledOnAllDescendants as bits, the scene's
IsHidden, AreAllDescendantsHidden, AreAnyDescendantsHidden, IsPickingDisabled,
IsPickingDisabledOnAllDescendants and IsPickingDisabledOnAnyDescendant, and IsCurrentStageIsolated.

Measured: an object's own state and its descendants' are kept apart; "with descendants" answers
need the object and all descendants, "all descendants" answers leave the object out (an object
without children has no hidden descendants and only visible ones). A new child of a hidden object
is visible. Isolation hides everything but the isolated objects in a state of its own, and
ExitIsolation brings back the visibility from before. Only `Show(Scene)` raises visibilityChanged,
at once; no operation raises pickingChanged. A null object or array throws ArgumentNullException.

Unmeasured: undo of visibility changes, Edit mode, objects hidden by HideFlags, other releases.

```sh
python3 tests/projects/scenevisibility/run.py record --label a
python3 tests/projects/scenevisibility/run.py record --label b
python3 tests/projects/scenevisibility/run.py publish a b
python3 tests/projects/scenevisibility/run.py gate
```

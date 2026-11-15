# Editor UI API without editor windows

Unbound has no editor windows, so editor GUI code runs as in the batch-mode Editor. `EditorUIProbe`
(an editor script, run when Play mode starts) calls the Editor UI API the packages use most — the
IMGUI editor state (indent, change checks, disabled and mixed-value scopes), metrics and content
(EditorGUIUtility), EditorStyles, the editor field functions outside OnGUI, property heights (with
property and decorator drawers, attributes, arrays as reorderable lists), editors (CreateEditor,
custom and built-in inspectors), generic menus and dialogs, tree views and multi-column headers,
drag and drop, handles and scene views, curve bindings and AnimationUtility — and records each
call's result, exception and logs.

Measured (see `managed/UnityEngine/Editor/EditorGUIState.cs`, `Editors.cs`, `TreeViews.cs`,
`AnimationUtility.cs`): the state and the measurements work anywhere; drawing outside OnGUI throws
(a NullReferenceException; IMGUI's own controls an ArgumentException), and so do EditorStyles in Play
mode; dialogs are cancelled; a TreeView's rows, expansion, search and selection live in its state;
editors get their targets before OnEnable; a non-legacy clip keeps a Transform's vector curves
whole. `Editor/Data/editor-icons-6000.0.txt` is the Editor's icon names (the textures of
`Editor/Data/Resources/unity editor resources`, listed by `tools/builtin-resources/names.py`).

Make the probe scene (once; it is committed):

    ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -quit -nographics -projectPath tests/projects/editorui \
        -executeMethod ProbeRunner.Create -logFile ~/.cache/editorui-create.log

Record Unity's reference (the Editor needs a display):

    EDITORUI_PROBE_OUT=$PWD/tests/projects/editorui/Reference \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/editorui \
        -executeMethod ProbeRunner.Record -logFile ~/.cache/editorui.log

Run Unbound and compare:

    EDITORUI_PROBE_OUT=/tmp/editorui unbound run -p tests/projects/editorui --headless 300000 --fixed-fps 60
    python3 tests/projects/editorui/compare.py tests/projects/editorui/Reference /tmp/editorui

`ClipLength.*` (the last calls, recorded with the macOS Editor, two identical captures; the other
rows are the Linux reference's, unchanged) records a clip's length, stop time and bindings while
AnimationClip.SetCurve and AnimationUtility.SetEditorCurve set, shorten and remove Transform vector
and other curves on legacy and non-legacy clips, the key times SetCurve leaves on a vector's
components, and how a clip of one position component plays.

`SetCurveKeys.*` (after them, recorded with the Linux Editor, two identical captures whose other
rows equal the reference's) records every key (time, value, tangents, weighted mode) of a
Transform vector's components after AnimationClip.SetCurve keys one component at times the others
lack: inside and outside each curve's keys, on a Hermite segment with unequal tangents, on stepped
segments (with a key 5e-7 s after another), and on a rotation, for legacy and non-legacy clips;
then key times about 1.9e-6 s and 2.1e-6 s apart and at 100 s, a component set alone with keys
5e-7 s apart, curves without keys, and the clip settings' range after the Euler angles' removal
and a later SetCurve.
Infinite tangents are recorded as "+inf"/"-inf".

Every call matches but one expected difference: Unity's reorderable list keeps its elements'
heights from its first layout, so an element expanded after the list was measured once does not
count yet. After SetCurve removes a clip's Euler angles by m_LocalEuler, the clip settings keep
the removed curves' range (stop time 2, with a length of 1); Unbound matches it.

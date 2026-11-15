# RectTransform dimension messages

Unity 6000.0.71f1 controls for `sendChildDimensionsChange`,
`OnRectTransformDimensionsChange` and `OnChildRectTransformDimensionsChange`.
The canonical reference contains 122 exact records from two independent GPU
Editor captures. The recorded device was NVIDIA RTX 4060; other devices were
not measured. The comparator preserves every differing, missing and extra value.

```sh
python3 tests/projects/rectmessages/run.py native --label repeat-a --release 6000.0.71f1
python3 tests/projects/rectmessages/run.py native --label repeat-b --release 6000.0.71f1
python3 tests/projects/rectmessages/run.py publish repeat-a repeat-b --release 6000.0.71f1
python3 tests/projects/rectmessages/run.py check --release 6000.0.71f1
```

## Contracts and observations

| Cause | Documentation for the selected release | Measured control and implementation |
| --- | --- | --- |
| Parent flag | [`RectTransform.sendChildDimensionsChange`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RectTransform-sendChildDimensionsChange.html) enables messages on components of the same GameObject when a child changes dimensions. | Default false. Only the direct parent receives the child message when its flag is true. A flagged grandparent receives no message for a grandchild alone. Changing the flag sends nothing. Cloning resets this runtime flag. |
| Message target and timing | [`OnChildRectTransformDimensionsChange`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnChildRectTransformDimensionsChange.html) reports a child dimension change. | The self message precedes the current parent's child message in the setter call. Disabled and inactive listeners receive both. Reparenting in the self callback selects the new parent. Repeated changes produce repeated calls; equal assignments produce none. |
| Descendant dimensions | [`UIBehaviour.OnRectTransformDimensionsChange`](https://docs.unity3d.com/Packages/com.unity.ugui@2.0/api/UnityEngine.EventSystems.UIBehaviour.html) describes propagation to child RectTransforms even if dimensions do not change. | The fixed-anchor control keeps its dimensions and receives no callback when its parent resizes. The stretched control changes dimensions and receives a callback. The implementation retains the measured rect-change comparison. Zero and negative dimensions remain supported. |
| Lifecycle | The same UIBehaviour page describes dimension callbacks before Awake, OnEnable and Start. | Resizing an inactive RectTransform after attaching a listener calls the dimension callback before activation delivers Awake and OnEnable. Creating the inactive listener alone sends no callback. A plain Transform is a control. |
| Layout rebuild | [uGUI 2.0 auto layout](https://docs.unity3d.com/Packages/com.unity.ugui@2.0/manual/UIAutoLayout.html) describes deferred rebuilding and `Canvas.ForceUpdateCanvases` for an immediate update. | HorizontalLayoutGroup enables the parent flag. A changed child's preferred size dirties ContentSizeFitter; the force stage changes the parent size. With the flag off, that parent update does not occur. Disabling the group clears its flag. All immediate/read/force/next-frame sequences remain in the reference. |
| Worker-thread access | The property is an engine binding. | Both getter and setter reject worker-thread access with the release's exact UnityException messages. |
| Transform state inside a callback | No global sibling callback order is claimed. | A stretched child's callback reads a previously cached descendant world position after the parent resizes. It sees the updated descendant matrix. A later sibling's cached matrix remains unchanged until its branch is traversed. Updating descendants before notifying their parent preserves the measured branch traversal and fixes stale descendant reads. |

These controls establish behavior for this release and these hierarchy/layout
contexts. They do not establish a global callback ordering contract across
unrelated hierarchies or configurable script execution orders. There is no
importer or cache-output change.

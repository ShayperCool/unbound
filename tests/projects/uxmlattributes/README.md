# UXML attribute values (Q17)

Measures what the Unity 6000.0.71f1 Editor builds from `Assets/Resources/UxmlAttributesProbe.uxml`
(one element per control) and compares the engine with it. The Editor runner re-imports the
documents after the controls compiled, recording the import's warnings and errors, then enters
Play mode, where `UxmlAttributesProbe` loads the document, clones it and records every named
element's class and probe properties plus the import, load and clone logs (sorted).

Controls (`Assets/Scripts/UxmlAttributesControls.cs`): a `[UxmlElement]` with one `[UxmlAttribute]`
of each type (int, float, bool, long, double, enum, Color, Vector2/3, Vector2Int, Rect, string,
char, UxmlTypeReference Type, List<int>, List<string>, Texture2D, Sprite, StyleSheet,
VisualTreeAsset, Object), custom and obsolete names, a derived control, a custom element name, a
control with only the legacy UxmlFactory/UxmlTraits, one with both, a control with Background and
Font members, Button `icon-image` and ListView/TreeView `item-template`, and preset controls (a
Button with an icon, a ListView with a template, Background, List<Background> and Texture2D members
with initial values) that tell a kept value from a cleared one.
Values: valid ones, project urls with guid and fileID, absolute, relative and guid-less paths,
references to another sub-asset or another type, missing assets, empty values, malformed numbers,
booleans, enums, colours (short hex, names, rgb()), vectors and list items, unknown types.

Documented (Unity 6000.0 Manual: [Create a custom control](https://docs.unity3d.com/6000.0/Documentation/Manual/UIE-create-custom-controls.html),
[Reference other files from UXML](https://docs.unity3d.com/6000.0/Documentation/Manual/UIE-reference-other-files-from-uxml.html)):
attribute names, `[UxmlAttribute]` custom/obsolete names, the built-in value types and url forms.
Observed in this capture only (the rules `managed/UnityEngine/UIElements/Uxml/UxmlAttributeValues.cs`
lists): values that do not convert silently take the type's default (white for a colour);
references to an object of another type resolve to the same file's object of the member's type,
else throw on assignment; missing or empty urls log the importer's warnings; `item-template` is read
as a VisualTreeAsset member is (an unresolved url clears it, another type keeps it); a Background
or List<Background> member (without a game converter) is not read at all, without warnings;
`icon-image` always replaces the icon with the Background made from the named object (an
unresolved url warns and clears it, an asset no Background can show clears it silently);
legacy traits still create their elements, and a `[UxmlElement]` wins over its legacy factory.

```sh
python3 tests/projects/uxmlattributes/run.py record --label native-a
python3 tests/projects/uxmlattributes/run.py record --label native-b
python3 tests/projects/uxmlattributes/run.py publish native-a native-b
python3 tests/projects/uxmlattributes/run.py gate
```

Values compare exactly. Font references use the built-in legacy font. VectorImage references are
not covered (they need an SVG importer package).

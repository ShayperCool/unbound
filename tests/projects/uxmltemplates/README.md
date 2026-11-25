# UXML template instances: attribute overrides and slots

Unity 6000.0.71f1, recorded with the macOS Editor in batch mode (managed element state and log text;
independent of the OS and the graphics device). The gate compares every value exactly (tolerance 0).
The Editor runner enters Play mode, where `Assets/Scripts/TemplatesProbe.cs` imports every document
again (`AssetDatabase.ImportAsset`, forced, each after the documents it instances: both runtimes log
a Play-mode import's messages in the call, tests/projects/importlogs), records the importer's
warnings and errors and what loading the documents then logs, clones the documents and records every
element (type, classes, tooltip, focus, inline width and the values the overrides touch) under its
path of names, with the logs of each clone.

- `Leaf.uxml`: named built-in elements (two labels of one name, a toggle, a box), an unnamed label, a
  `[UxmlElement]` game element (`NewElement`, two `[UxmlAttribute]` members), a game element of the
  deprecated factory path (`OldElement`: one attribute read through its description with a former
  name, one read straight from the bag) and two slots (`slot-name`);
- `Middle.uxml` instances Leaf with overrides of each kind of element (an attribute the element has
  and one it lacks, two overrides of one attribute in the instance, an override of the name of one of
  the instance's own children), fills both slots and a missing one, and defines a slot itself;
- `Outer.uxml` instances Middle with overrides of the same elements (an outer override wins over
  Middle's; under a former name too, on the factory element and on the `[UxmlElement]` one), overrides of `name`, `class` and `style` (reported, dropped), of
  the nested instance element and of a name no element has, and fills Middle's slot and one of Leaf's
  (not reachable from Outer);
- `Host.uxml`: an instance of a template with a `content-container`, and a template container written
  as an element (`<ui:TemplateContainer template=...>`, which names its template without cloning it);
- `DuplicateSlots.uxml` instances `Slots.uxml`, which defines one slot name twice (reported; the
  first element keeps it);
- `OldTemplateDoc.uxml`: a game template container of the deprecated factory path (TemplateContainer's
  traits clone its template), cloned alone, inside an instance without overrides (`OldTemplateBare`)
  and inside instances with overrides (`OldTemplateHost`, `OldTemplateOther`, whose override names no
  element), where the Editor's clone throws a NullReferenceException; also over a template holding
  an instance (`NestedOld`, which throws) and over a template of deprecated-factory elements only
  under overrides (`OldFactoryHost`, which clones and takes the override);
- `AssetHost.uxml` instances `Sub/Card.uxml` and overrides asset references (a ListView's
  `item-template`, a `[UxmlAttribute]` and a factory's asset description) with a path relative to
  AssetHost's own folder;
- `SlotClash.uxml` defines a slot its nested instance's document defines again (the clone throws);
  `EarlyHost.uxml` instances `EarlySlot.uxml`, whose slot defined before its nested instance is no
  longer reachable and whose later one is;
- `Odd.uxml`: overrides without `element-name`, `<AttributeOverrides>` under a plain element,
  `slot` on a child of a plain element and an unknown element in a namespace;
- `import.flags.*`: each document's `importedWithWarnings` and `importedWithErrors`.

The importer's id of an element in its messages (`(Type)(id)`) is an internal number and is recorded
as `#`.

Other releases are unmeasured.

# UXML template instances: attribute overrides and slots

Unity 6000.0.71f1, recorded with the macOS Editor in batch mode (managed element state and log
text; independent of the OS and the graphics device). The gate compares every value exactly
(tolerance 0). The Editor runner re-imports the documents while recording the importer's warnings
and errors, then enters Play mode, where `Assets/Scripts/TemplatesProbe.cs` loads every document
(Unbound imports a document when it is loaded), records the import logs, clones the documents and
records every element (type, classes, tooltip, focus, inline width and the values the overrides
touch) under its path of names, with the logs of each clone.

- `Leaf.uxml`: named built-in elements (two labels of one name, a toggle, a box), an unnamed label, a
  `[UxmlElement]` game element (`NewElement`, two `[UxmlAttribute]` members), a game element of the
  deprecated factory path (`OldElement`: one attribute read through its description with a former
  name, one read straight from the bag) and two slots (`slot-name`);
- `Middle.uxml` instances Leaf with overrides of each kind of element (an attribute the element has
  and one it lacks, two overrides of one attribute in the instance, an override of the name of one of
  the instance's own children), fills both slots and a missing one, and defines a slot itself;
- `Outer.uxml` instances Middle with overrides of the same elements (an outer override wins over
  Middle's; under a former name too), overrides of `name`, `class` and `style` (reported, dropped), of
  the nested instance element and of a name no element has, and fills Middle's slot and one of Leaf's
  (not reachable from Outer);
- `Host.uxml`: an instance of a template with a `content-container`, and a template container written
  as an element (`<ui:TemplateContainer template=...>`, which names its template without cloning it);
- `DuplicateSlots.uxml` instances `Slots.uxml`, which defines one slot name twice (reported; the
  first element keeps it);
- `OldTemplateDoc.uxml`: a game template container of the deprecated factory path (TemplateContainer's
  traits clone its template), cloned alone, inside an instance without overrides (`OldTemplateBare`)
  and inside instances with overrides (`OldTemplateHost`, `OldTemplateOther`, whose override names no
  element), where the Editor's clone throws a NullReferenceException.

Other releases are unmeasured.

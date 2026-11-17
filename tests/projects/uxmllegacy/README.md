# Deprecated UXML factories and traits

Unity 6000.0.71f1, recorded with the macOS Editor in batch mode (managed element state and
attribute descriptions; independent of the OS and the graphics device). The gate compares every row
exactly.

Older custom controls declare a `UxmlFactory<T, TTraits>` over the traits of `VisualElement`,
`BindableElement` or a built-in control. `Resources/Legacy.uxml` creates game elements through such
factories, each giving the element state of its own in its constructor, once without attributes and
once with them:

- `element.*Empty`: a traits class's Init sets every property it describes, from the description's
  default when the attribute is absent, so the constructor's state is replaced (name, view-data key
  and tooltip become empty, picking mode, usage hints, focus and enabled state their defaults, a
  ScrollView's sizes and rates, a ListView's options, a TextField's options...). The exceptions keep
  the element's value: an empty binding path, a list's item height and item template, a scroll view's
  scroller visibility, the former focus attribute;
- `element.*Full`, `element.*Obsolete`: the attributes, under their names and former names;
  `vectorValue`: a composite field's traits read its components, not `value`;
- `element.multiColumn*`: a multi-column list view subclass with its own sorting mode, column and sort
  description: without the attribute the sorting mode is kept, the former boolean attribute reads true as
  Custom and false as None, and the columns and sort descriptions are cleared;
- `element.custom*`: a game traits class over Button's, its own attribute read after the base's;
- `element.enum*`: an EnumField subclass through EnumField's traits: a type alone (the zero value), a
  value (exactly as named; another case is not a value), an undefined value (reported, no value), no type;
- `element.builtin*`: built-in elements (not through a factory): text inputs (a text field, an integer
  field) read `placeholder-text`, `hide-placeholder-on-focus`, `select-word-by-double-click` and
  `select-line-by-triple-click`; a text element reads `selectable`, `double-click-selects-word` and
  `triple-click-selects-line` (also under their former names);
- `control.*`: a text field's vertical scroller visibility through multiline and password changes;
- `traits.*`: every built-in traits class's attribute descriptions in the order the Editor lists them
  (base classes first): type, name, default, former names, use, XML type; and their child
  descriptions.

Other releases are unmeasured.

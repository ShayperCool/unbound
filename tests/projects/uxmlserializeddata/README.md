# UXML serialized data and attribute flag selection (Q628)

Measures Unity 6's `UxmlSerializedData` mechanism on Unity 6000.0.71f1:
`UxmlSerializedData.ShouldWriteAttributeValue`, `UxmlAttributeFlags` (`Ignore`,
`OverriddenInUxml`, `DefaultValue`), `Deserialize(object)` and internal flag-scoped
`Deserialize(object, UxmlAttributeFlags)` (including flag restoration after exceptions),
source-generated `[UxmlElement]` / `[UxmlAttribute]` and `[UxmlObject]` `UxmlSerializedData`
deserialization, built-in base `UxmlSerializedData` types (`VisualElement`, `BindableElement`,
`TextElement`, `Image`, `Binding`, `CustomBinding`, `DataBinding`), `UxmlAttributeNames`,
`UxmlDescriptionCache`, `RegisterUxmlCacheAttribute`, `UxmlIgnoreAttribute`,
`UxmlObjectReferenceAttribute`, and `UxmlSerializedDataUtility.CopySerialized`, together with
an ordinary `VisualTreeAsset.CloneTree` control over `Assets/Resources/UxmlSerializedDataProbe.uxml`.

Documented (Unity 6000.0 Scripting API:
[UxmlSerializedData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.UxmlSerializedData.html),
[UxmlSerializedData.ShouldWriteAttributeValue](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.UxmlSerializedData.ShouldWriteAttributeValue.html),
[UxmlSerializedData.UxmlAttributeFlags](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.UxmlSerializedData.UxmlAttributeFlags.html),
[UxmlAttributeNames](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.UxmlAttributeNames.html),
[UxmlDescriptionCache](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.UxmlDescriptionCache.html),
[UxmlObjectReferenceAttribute](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.UxmlObjectReferenceAttribute.html),
[UxmlSerializedDataUtility](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.UxmlSerializedDataUtility.html)):
- `ShouldWriteAttributeValue` returns true when the attribute's flags match the active deserialize
  mask (default `OverriddenInUxml`), so `Ignore` preserves constructor/preset values,
  `OverriddenInUxml` writes explicit values (including zero/empty/false), and `DefaultValue` is
  skipped under default `Deserialize(object)` and written under `DefaultValue` mask.
- `UxmlSerializedDataUtility.CopySerialized` produces an independent serialized clone for custom
  reference-type attributes.
- `UxmlDescriptionCache.RegisterType` stores `UxmlAttributeNames[]` per `UxmlSerializedData` type.

Observed in the Unity 6000.0.71f1 reference capture:
- `UxmlObjectReferenceAttribute(string)` throws `ArgumentException` for `"null"`, whereas the
  two-argument constructor `UxmlObjectReferenceAttribute(string, params Type[])` accepts `"null"`.
- `UxmlAttributeNames` normalizes a null `obsoleteNames` argument to an empty array.

```sh
python3 tests/projects/uxmlserializeddata/run.py record --label native-a
python3 tests/projects/uxmlserializeddata/run.py record --label native-b
python3 tests/projects/uxmlserializeddata/run.py publish native-a native-b
python3 tests/projects/uxmlserializeddata/run.py gate
```

All values compare exactly (`tolerance = 0`).

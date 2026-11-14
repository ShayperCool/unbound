# UXML converter dispatch

Unity 6000.0.71f1, 57 exact records from two independent GPU Editor captures.
The captures used the NVIDIA driver only.

The [selected converter API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.UxmlAttributeConverter_1.html)
converts custom attribute values between text and their declared type. Its
[FromString](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.UxmlAttributeConverter_1.FromString.html)
and [ToString](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.UxmlAttributeConverter_1.ToString.html)
methods are virtual dispatch points. The internal object-valued bridge uses those
same typed methods and instance state.

The probe changes converter state between calls, supplies two distinct document
contexts, and distinguishes a hidden derived method from the inherited virtual
implementation. It records malformed input, invalid object casts, null value and
reference types, nullable boxing, and default formatting under English and French
cultures. Context independence and exception details are selected-release observations.
The bridge preserves the existing typed default formatting.

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/uxmlconverter/run.py native --label first
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/uxmlconverter/run.py native --label second
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/uxmlconverter/run.py publish first second
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/uxmlconverter/run.py check
```

The gate reads the committed reference and compares the complete union of keys
without tolerance. Engine outputs and capture repeats stay outside Git. Managed
tests additionally exercise custom attribute discovery, cached converter instances,
inherited virtual dispatch, constructor errors and a converter that deliberately
throws a TargetInvocationException.

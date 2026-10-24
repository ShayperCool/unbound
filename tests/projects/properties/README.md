# Properties probe

Unity.Properties (UnityEngine.PropertiesModule) and com.unity.serialization 3.1.2 run by the same
Editor script in the Editor and in Unbound when Play mode is entered
(`Assets/Editor/PropertiesProbe.cs`): which members reflected property bags take and in which
order (fields, [SerializeField], [CreateProperty], [DontCreateProperty], [NonSerialized],
read-only, inheritance), source-generated bags ([GeneratePropertyBag]), the built-in bags (vectors,
colors, rects, bounds, System.Version), collection bags, property paths (parsing, printing,
errors, combining), PropertyContainer get/set/validity by path with conversions and every error
code and exception, visitors with list/dictionary overrides, exclusion and adapters,
TypeConversion between primitives, strings, enums, nullables and objects, TypeUtility, and JSON
written and read by the serialization package.

Record Unity's reference (`PropertiesProbe.CreateScene` made the scene):

    PROPERTIES_PROBE_OUT=$PWD/tests/projects/properties/Reference/unity-6000.0.71f1.json DISPLAY=:0 \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/properties \
        -executeMethod PropertiesProbe.Record -logFile /tmp/properties.log

Run Unbound and compare (`compare.py` words ArgumentException messages as Mono does):

    PROPERTIES_PROBE_OUT=/tmp/properties.json unbound run -p tests/projects/properties --headless 5 --no-watch
    python3 tests/projects/properties/compare.py tests/projects/properties/Reference/unity-6000.0.71f1.json /tmp/properties.json

All 117 records match.

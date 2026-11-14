# Property enumerator state

This probe records 194 exact values in Unity 6000.0.71f1 from two independent GPU
Editor runs on the NVIDIA driver. It compares strings and integers exactly and reports
every missing, extra or different value. Existing references and tolerances are unchanged.

The Unity 6000.0 [Enumerator API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Unity.Properties.PropertyCollection_1.Enumerator.html)
describes Current as the element at the current position, MoveNext as advancing the
underlying enumerator, Reset as returning to its initial position and Dispose as
disposing the underlying enumerator. The observable source counter and changed position
controls distinguish the actual calls. The package documentation does not establish
the internal cache behavior. The following are selected-release observations:

- Current starts null and reads the underlying getter only during MoveNext.
- Each advance reads and stores Current even when MoveNext returns false. A getter
  exception occurs during MoveNext and preserves the previous cache.
- Changing the source after an advance does not change cached Current.
- Private cache writes survive Reset and Dispose; a later advance overwrites them.
- Enumerable reset restarts its source; indexed reset restarts the index. The selected
  list form resets a boxed copy, so its next advance continues its original position.
  This measured list limitation differs from the general Reset description.
- Struct copies have independent cached references. An indexed source still reuses
  its mutable element-property object; caching a reference does not freeze that object.
- Default and empty-list forms distinguish an absent source from a completed one.

Run with the selected release explicitly:

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/propertyenumerator/run.py native --label native-a
python3 tests/projects/propertyenumerator/run.py native --label native-b
python3 tests/projects/propertyenumerator/run.py publish native-a native-b
python3 tests/projects/propertyenumerator/run.py check
```

The runner uses independent project copies under the validation cache, checks GPU
Editor results before publication and replays only the engine in the gate. The API
change removes the exact cached-setter exception without changing default-state
policies, serializer classification, source ownership or the underlying reset/dispose
implementations. Other releases and corrupted private source kinds are unmeasured.

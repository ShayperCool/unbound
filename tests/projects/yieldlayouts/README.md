# Yield wrapper layouts

Unity 6000.0.71f1, recorded twice with the NVIDIA GPU. Both canonical captures contain 37 byte-identical rows. The gate runs only the engine and compares every missing, extra or unequal value exactly.

The selected assemblies declare YieldInstruction and WaitForSeconds sequentially. Their complete instance payloads are an empty base and one float, respectively. The probe checks layout, declared size, runtime packing, Marshal.SizeOf, the float offset, constructor values, raw unmanaged writes and overwrites of existing instances. Inputs include nondefault finite values, negative zero, NaN and both infinities. A derived automatic-layout class and the fixed-update/end-of-frame subclasses remain valid.

The [YieldInstruction](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/YieldInstruction.html) and [WaitForSeconds](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/WaitForSeconds.html) APIs describe coroutine yield objects. WaitForSeconds uses scaled time and starts waiting at the end of the current frame; this layout change does not change wait timing. [StartCoroutine](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.StartCoroutine.html) runs an iterator until its first yield. The control changes its counter before that yield and confirms the returned handle. [StopCoroutine](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.StopCoroutine.html) stops the specified iterator. The probe stops a handle that would increment again on the next update, then observes the unchanged counter after two further updates. This distinguishes a real stop from merely checking immediately after the call.

The Editor reports a one-byte empty base and a four-byte wait payload at offset zero. All six float bit patterns survive both marshalling directions. A live Coroutine subclass can still be created and stopped after the base layout migration; its own pointer/ownership layout remains a separate migration. GUIContent also remains queued because its complete payload includes an additional serialized cache and an event field.

## Default packing discrepancy

The selected assemblies encode default packing as zero, and the Editor exposes runtime packing eight. CoreCLR exposes zero for the same default metadata. Explicit packing eight matches all 37 runtime controls. Both exact declaration discrepancies remain in ApiCheckAllowList for this release, recording the selected metadata value unchanged. Removing them requires a shared reflection/default-packing bridge; the comparator does not normalize or hide them. [StructLayoutAttribute.Pack](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.structlayoutattribute.pack) documents zero as the platform default. Other wrapper hierarchies and other releases remain unmeasured by this probe.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/yieldlayouts/run.py native --label reference-a
python3 tests/projects/yieldlayouts/run.py native --label reference-b
python3 tests/projects/yieldlayouts/run.py publish reference-a reference-b
python3 tests/projects/yieldlayouts/run.py check
```

Captures, raw comparisons and engine outputs stay in the run cache. Existing probe references and tolerances remain unchanged.

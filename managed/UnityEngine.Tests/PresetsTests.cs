using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Unbound.Tests;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEditor.Presets;
using Xunit;

namespace UnityEngine.Tests
{
    public class PresetsTests
    {
        sealed class PresetScript : MonoBehaviour
        {
            public int count = 3;
            public float speed = 1.5f;
            public string label = "default";
            public Color tint = Color.red;
            [SerializeField] int hidden = 7;
            internal int Hidden { get => hidden; set => hidden = value; }
        }

        sealed class CurveScript : MonoBehaviour
        {
            public AnimationCurve curve = AnimationCurve.Linear(0, 0, 1, 1);
        }

        [Serializable]
        sealed class ManagedPayload
        {
            public int x = 7;
        }

        sealed class ManagedRefScript : MonoBehaviour
        {
            [SerializeReference] public object item = new ManagedPayload();
        }

        sealed class PresetData : ScriptableObject
        {
            public int value = 4;
        }

        sealed class SampleScriptedImporter : ScriptedImporter
        {
            public override void OnImportAsset(AssetImportContext ctx) { }
        }

        [ExcludeFromPreset]
        sealed class ExcludedScript : MonoBehaviour { }

        sealed class LogCapture : ILogHandler, IDisposable
        {
            readonly ILogHandler m_Previous;
            internal readonly List<string> Lines = new List<string>();
            internal LogCapture()
            {
                m_Previous = Debug.unityLogger.logHandler;
                Debug.unityLogger.logHandler = this;
            }
            public void LogFormat(LogType logType, Object context, string format, params object[] args) =>
                Lines.Add($"{logType}: {string.Format(format, args)}");
            public void LogException(Exception exception, Object context) =>
                Lines.Add($"Exception: {exception.GetType().Name}: {exception.Message}");
            public void Dispose() => Debug.unityLogger.logHandler = m_Previous;
        }

        [Fact]
        public void PresetTypeDistinguishesValidDefaultExcludedScriptedImporterAndMissingScript()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var go = new GameObject("Target");
            var script = go.AddComponent<PresetScript>();
            var excluded = go.AddComponent<ExcludedScript>();
            var box = go.AddComponent<BoxCollider>();
            var data = ScriptableObject.CreateInstance<PresetData>();
            var clip = new AnimationClip();
            var tex = new Texture2D(2, 2);
            var texImporter = (TextureImporter)ObjectConstruction.CreateWithBacking(typeof(TextureImporter));
            var scriptedImporter = (SampleScriptedImporter)ObjectConstruction.CreateWithBacking(typeof(SampleScriptedImporter));
            var preset = new Preset(go.transform);
            try
            {
                var transformType = new PresetType(go.transform);
                Assert.True(transformType.IsValid());
                Assert.True(transformType.IsValidDefault());
                Assert.Equal("UnityEngine.Transform", transformType.GetManagedTypeName());

                var scriptType = new PresetType(script);
                Assert.True(scriptType.IsValid());
                Assert.True(scriptType.IsValidDefault());
                Assert.Equal(typeof(PresetScript).FullName, scriptType.GetManagedTypeName());

                var dataType = new PresetType(data);
                Assert.True(dataType.IsValid());
                Assert.True(dataType.IsValidDefault());

                var clipType = new PresetType(clip);
                Assert.True(clipType.IsValid());
                Assert.False(clipType.IsValidDefault());
                Assert.Equal("UnityEngine.AnimationClip", clipType.GetManagedTypeName());

                // TextureImporter must resolve cleanly without logging an unsupported class warning.
                using (var logs = new LogCapture())
                {
                    var importerType = new PresetType(texImporter);
                    Assert.True(importerType.IsValid());
                    Assert.True(importerType.IsValidDefault());
                    Assert.Equal("UnityEditor.TextureImporter", importerType.GetManagedTypeName());
                    Assert.DoesNotContain(logs.Lines, line => line.Contains("is not supported yet"));
                }

                // ScriptedImporter subclasses are valid default PresetTypes.
                var scriptedType = new PresetType(scriptedImporter);
                Assert.True(scriptedType.IsValid());
                Assert.True(scriptedType.IsValidDefault());
                Assert.Equal(typeof(SampleScriptedImporter).FullName, scriptedType.GetManagedTypeName());

                // Missing MonoScript (native id 114, null script, empty fallback) is invalid yet reports MonoBehaviour.
                var missingScriptType = new PresetType(114, null, "");
                Assert.False(missingScriptType.IsValid());
                Assert.False(missingScriptType.IsValidDefault());
                Assert.Equal("UnityEngine.MonoBehaviour", missingScriptType.GetManagedTypeName());

                Assert.False(new PresetType(go).IsValid());
                Assert.False(new PresetType(go).IsValidDefault());
                Assert.False(new PresetType(excluded).IsValid());
                Assert.False(new PresetType(tex).IsValid());
                Assert.False(new PresetType(preset).IsValid());
                Assert.False(default(PresetType).IsValid());
                Assert.Equal("UnityEngine.Object", default(PresetType).GetManagedTypeName());

                Assert.Equal(transformType, new PresetType(go.transform));
                Assert.NotEqual(transformType, new PresetType(box));
                Assert.Throws<ArgumentNullException>(() => new PresetType((Object)null));
            }
            finally
            {
                Object.DestroyImmediate(preset);
                Object.DestroyImmediate(scriptedImporter);
                Object.DestroyImmediate(texImporter);
                Object.DestroyImmediate(tex);
                Object.DestroyImmediate(clip);
                Object.DestroyImmediate(data);
                Object.DestroyImmediate(go);
            }
        }

        [Fact]
        public void DefaultPresetConstructorsAndDefaultValueFollowUnity()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var go = new GameObject("Box");
            var preset = new Preset(go.transform);
            try
            {
                var two = new DefaultPreset("f", preset);
                Assert.Equal("f", two.filter);
                Assert.Same(preset, two.preset);
                Assert.True(two.enabled);

                var three = new DefaultPreset("g", preset, false);
                Assert.Equal("g", three.filter);
                Assert.Same(preset, three.preset);
                Assert.False(three.enabled);

                var def = default(DefaultPreset);
                Assert.Null(def.filter);
                Assert.Null(def.preset);
                Assert.True(def.enabled);
            }
            finally
            {
                Object.DestroyImmediate(preset);
                Object.DestroyImmediate(go);
            }
        }

        [Fact]
        public void PresetCapturesAndAppliesTransformBoxAndScriptProperties()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var srcGo = new GameObject("Source");
            var dstGo = new GameObject("Dest");
            var partialGo = new GameObject("Partial");
            Preset transformPreset = null, scriptPreset = null, boxPreset = null;
            try
            {
                srcGo.transform.localPosition = new Vector3(1, 2, 3);
                srcGo.transform.localScale = new Vector3(2, 3, 4);
                transformPreset = new Preset(srcGo.transform);
                Assert.True(transformPreset.IsValid());
                Assert.Equal("UnityEngine.Transform", transformPreset.GetTargetFullTypeName());
                Assert.Equal("Transform", transformPreset.GetTargetTypeName());
                Assert.True(transformPreset.CanBeAppliedTo(dstGo.transform));
                Assert.False(transformPreset.CanBeAppliedTo(null));
                Assert.False(transformPreset.CanBeAppliedTo(dstGo));

                Assert.True(transformPreset.ApplyTo(dstGo.transform));
                Assert.Equal(new Vector3(1, 2, 3), dstGo.transform.localPosition);
                Assert.Equal(new Vector3(2, 3, 4), dstGo.transform.localScale);
                Assert.True(transformPreset.DataEquals(dstGo.transform));

                // Partial apply: only m_LocalScale changes.
                Assert.True(transformPreset.ApplyTo(partialGo.transform, new[] { "m_LocalScale" }));
                Assert.Equal(Vector3.zero, partialGo.transform.localPosition);
                Assert.Equal(new Vector3(2, 3, 4), partialGo.transform.localScale);
                Assert.False(transformPreset.DataEquals(partialGo.transform));

                // Excluded property is skipped on ApplyTo and ignored by DataEquals (measured).
                transformPreset.excludedProperties = new[] { "m_LocalPosition" };
                Assert.True(transformPreset.DataEquals(partialGo.transform));

                // Script fields (including private [SerializeField]) and UpdateProperties.
                var srcScript = srcGo.AddComponent<PresetScript>();
                srcScript.count = 9;
                srcScript.label = "preset";
                srcScript.tint = Color.green;
                srcScript.Hidden = 42;
                scriptPreset = new Preset(srcScript);
                var dstScript = dstGo.AddComponent<PresetScript>();
                Assert.True(scriptPreset.ApplyTo(dstScript));
                Assert.Equal((9, "preset", Color.green, 42), (dstScript.count, dstScript.label, dstScript.tint, dstScript.Hidden));

                srcScript.count = 15;
                Assert.True(scriptPreset.UpdateProperties(srcScript));
                Assert.True(scriptPreset.ApplyTo(dstScript));
                Assert.Equal(15, dstScript.count);

                // BoxCollider.
                var srcBox = srcGo.AddComponent<BoxCollider>();
                srcBox.size = new Vector3(4, 5, 6);
                srcBox.isTrigger = true;
                var dstBox = dstGo.AddComponent<BoxCollider>();
                boxPreset = new Preset(srcBox);
                Assert.False(transformPreset.ApplyTo(dstBox));
                Assert.True(boxPreset.ApplyTo(dstBox));
                Assert.Equal(new Vector3(4, 5, 6), dstBox.size);
                Assert.True(dstBox.isTrigger);

                // Error paths (RemoveFromDefault(null) throws NullReferenceException as in Unity).
                Assert.Throws<ArgumentNullException>(() => new Preset(null));
                Assert.Throws<NotSupportedException>(() => new Preset(srcGo));
                Assert.Throws<ArgumentNullException>(() => transformPreset.ApplyTo(null));
                Assert.Throws<ArgumentNullException>(() => Preset.GetDefaultPresetsForObject(null));
                Assert.Throws<NullReferenceException>(() => Preset.RemoveFromDefault(null));
            }
            finally
            {
                if (boxPreset != null) Object.DestroyImmediate(boxPreset);
                if (scriptPreset != null) Object.DestroyImmediate(scriptPreset);
                if (transformPreset != null) Object.DestroyImmediate(transformPreset);
                Object.DestroyImmediate(partialGo);
                Object.DestroyImmediate(dstGo);
                Object.DestroyImmediate(srcGo);
            }
        }

        [Fact]
        public void UnmappedTypesUnserializedLeafFieldsCoupledPresetsAndCompoundFiltersReportLoudly()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var clip = new AnimationClip();
            var rectGo = new GameObject("Rect", typeof(RectTransform));
            var newlineGo = new GameObject("Rect\n");
            var curveGo = new GameObject("Curve");
            var curveScript = curveGo.AddComponent<CurveScript>();
            var managedRefGo = new GameObject("ManagedRef");
            var managedRefScript = managedRefGo.AddComponent<ManagedRefScript>();
            var texImporter = (TextureImporter)AssetImporter.FromMeta(
                "Assets/T.png",
                "0123456789abcdef0123456789abcdef",
                "TextureImporter",
                TestYaml.DocumentBody("TextureImporter:\n  externalObjects: {}\n  serializedVersion: 13\n  userData: \n"));
            var shader = Shader.Create("PresetsTests/Variant", 2000);
            var parentMat = new Material(shader);
            var variantMat = new Material(shader) { parent = parentMat };
            var data = ScriptableObject.CreateInstance<PresetData>();
            Preset clipPreset = null, rectPreset = null, curvePreset = null, managedRefPreset = null;
            Preset texPreset = null, variantPreset = null, coupledPreset = null;
            string clipMember = "UnityEditor.Presets.Preset (serialized properties of UnityEngine.AnimationClip)";
            string rectMember = "UnityEditor.Presets.Preset (serialized properties of UnityEngine.RectTransform)";
            string texCaptureMember = "UnityEditor.Presets.Preset (serialized properties of UnityEditor.TextureImporter)";
            string texApplyMember = "UnityEditor.Presets.Preset.ApplyTo (serialized properties of UnityEditor.TextureImporter)";
            string variantCaptureMember = "UnityEditor.Presets.Preset (serialized properties of UnityEngine.Material (variant))";
            string variantApplyMember = "UnityEditor.Presets.Preset.ApplyTo (serialized properties of UnityEngine.Material (variant))";
            string curveMember = $"UnityEditor.Presets.Preset (property type AnimationCurve on {typeof(CurveScript).FullName}.curve)";
            string managedRefMember = $"UnityEditor.Presets.Preset (property type ManagedReference on {typeof(ManagedRefScript).FullName}.item)";
            string coupledMember = "UnityEditor.Presets.Preset (coupled presets)";
            string filterMember = "UnityEditor.Presets.Preset.GetDefaultPresetsForObject (a filter other than single-token glob:\"...\" on a component's GameObject name)";
            string[] allMembers =
            {
                clipMember, rectMember, texCaptureMember, texApplyMember,
                variantCaptureMember, variantApplyMember, curveMember, managedRefMember,
                coupledMember, filterMember,
            };
            foreach (var m in allMembers) Stubs.Forget(m);
            try
            {
                using var logs = new LogCapture();
                clipPreset = new Preset(clip);
                Assert.Contains(logs.Lines, line => line.Contains("Unbound:") && line.Contains(clipMember));

                rectPreset = new Preset(rectGo.GetComponent<RectTransform>());
                Assert.Contains(logs.Lines, line => line.Contains("Unbound:") && line.Contains(rectMember));

                // .meta-backed TextureImporter (TypeTreeTargets.Of != null) reports loudly on both capture and apply.
                texPreset = new Preset(texImporter);
                Assert.Contains(logs.Lines, line => line.Contains("Unbound:") && line.Contains(texCaptureMember));
                Assert.True(texPreset.ApplyTo(texImporter));
                Assert.Contains(logs.Lines, line => line.Contains("Unbound:") && line.Contains(texApplyMember));

                // Material variants report loudly on both capture and apply.
                Assert.True(variantMat.isVariant);
                variantPreset = new Preset(variantMat);
                Assert.Contains(logs.Lines, line => line.Contains("Unbound:") && line.Contains(variantCaptureMember));
                Assert.True(variantPreset.ApplyTo(variantMat));
                Assert.Contains(logs.Lines, line => line.Contains("Unbound:") && line.Contains(variantApplyMember));

                curvePreset = new Preset(curveScript);
                Assert.Contains(logs.Lines, line => line.Contains("Unbound:") && line.Contains(curveMember));
                Assert.DoesNotContain(curvePreset.PropertyModifications, p => p.propertyPath == "curve");

                // Non-null [SerializeReference] field reports loudly instead of recursing silently into child fields.
                managedRefPreset = new Preset(managedRefScript);
                Assert.Contains(logs.Lines, line => line.Contains("Unbound:") && line.Contains(managedRefMember));
                Assert.DoesNotContain(managedRefPreset.PropertyModifications, p => p.propertyPath.StartsWith("item.", StringComparison.Ordinal));

                // Coupled preset YAML reports loudly and sets IsCoupled; null resolver with objectReference does not throw.
                var docs = UnityYaml.Parse(@"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!181963792 &1
Preset:
  m_Name: Coupled
  m_TargetType:
    m_NativeTypeID: 4
    m_ManagedTypePPtr: {fileID: 0}
    m_ManagedTypeFallback: 
  m_Properties:
  - target: {fileID: 0}
    propertyPath: m_Ref
    value: 
    objectReference: {fileID: 123, guid: 0123456789abcdef0123456789abcdef, type: 3}
  m_ExcludedProperties: []
  m_CoupledType:
    m_NativeTypeID: 20
    m_ManagedTypePPtr: {fileID: 0}
    m_ManagedTypeFallback: 
  m_CoupledProperties: []
");
                coupledPreset = new Preset();
                coupledPreset.ReadSerialized(docs[0].Body, null);
                Assert.True(coupledPreset.IsCoupled());
                Assert.Contains(logs.Lines, line => line.Contains("Unbound:") && line.Contains(coupledMember));

                // PresetType equality distinguishes null vs empty fallback (matching Unity's PresetType.Equals).
                Assert.False(default(PresetType).Equals(new PresetType(0, null, "")));

                // Single-token glob matches exact end (\z, not before trailing \n); compound, unquoted, **, ?, and non-component targets report loudly.
                Assert.True(Preset.FilterTakes("glob:\"Rect*\"", rectGo.transform));
                Assert.False(Preset.FilterTakes("glob:\"Rect\"", newlineGo.transform));
                Assert.False(Preset.FilterTakes("glob:\"Rect*\" t:Transform", rectGo.transform));
                Assert.Contains(logs.Lines, line => line.Contains("Unbound:") && line.Contains(filterMember));
                Stubs.Forget(filterMember);
                logs.Lines.Clear();
                Assert.False(Preset.FilterTakes("glob:Rect*", rectGo.transform));
                Assert.Contains(logs.Lines, line => line.Contains("Unbound:") && line.Contains(filterMember));
                Stubs.Forget(filterMember);
                logs.Lines.Clear();
                Assert.False(Preset.FilterTakes("glob:\"Rect?\"", rectGo.transform));
                Assert.Contains(logs.Lines, line => line.Contains("Unbound:") && line.Contains(filterMember));
                Stubs.Forget(filterMember);
                logs.Lines.Clear();
                Assert.False(Preset.FilterTakes("glob:\"Rect**\"", rectGo.transform));
                Assert.Contains(logs.Lines, line => line.Contains("Unbound:") && line.Contains(filterMember));
                Stubs.Forget(filterMember);
                logs.Lines.Clear();
                Assert.False(Preset.FilterTakes("glob:\"Preset*\"", data));
                Assert.Contains(logs.Lines, line => line.Contains("Unbound:") && line.Contains(filterMember));
            }
            finally
            {
                foreach (var m in allMembers) Stubs.Forget(m);
                if (coupledPreset != null) Object.DestroyImmediate(coupledPreset);
                if (variantPreset != null) Object.DestroyImmediate(variantPreset);
                if (texPreset != null) Object.DestroyImmediate(texPreset);
                if (managedRefPreset != null) Object.DestroyImmediate(managedRefPreset);
                if (curvePreset != null) Object.DestroyImmediate(curvePreset);
                if (rectPreset != null) Object.DestroyImmediate(rectPreset);
                if (clipPreset != null) Object.DestroyImmediate(clipPreset);
                Object.DestroyImmediate(data);
                Object.DestroyImmediate(variantMat);
                Object.DestroyImmediate(parentMat);
                Object.DestroyImmediate(texImporter);
                Object.DestroyImmediate(managedRefGo);
                Object.DestroyImmediate(curveGo);
                Object.DestroyImmediate(newlineGo);
                Object.DestroyImmediate(rectGo);
                Object.DestroyImmediate(clip);
            }
        }

        [Fact]
        public void UnmeasuredReleaseReportsLoudlyAcrossPresetApisWithoutThrowing()
        {
            var go = new GameObject("Unmeasured");
            Preset preset = null;
            string[] members =
            {
                "UnityEditor.Presets.PresetType.ctor",
                "UnityEditor.Presets.Preset.ctor",
                "UnityEditor.Presets.Preset.ApplyTo",
                "UnityEditor.Presets.Preset.DataEquals",
                "UnityEditor.Presets.Preset.UpdateProperties",
                "UnityEditor.Presets.Preset.GetAllDefaultTypes",
                "UnityEditor.Presets.Preset.GetDefaultPresetsForType",
                "UnityEditor.Presets.Preset.GetDefaultPresetsForObject",
            };
            foreach (var m in members) Stubs.Forget(m);
            try
            {
                using (TestRelease.Pin(TestRelease.Unmeasured))
                using (var logs = new LogCapture())
                {
                    var pt = new PresetType(go.transform);
                    Assert.False(pt.IsValid());
                    preset = new Preset(go.transform);
                    Assert.False(preset.IsValid());
                    Assert.False(preset.ApplyTo(go.transform));
                    Assert.False(preset.DataEquals(go.transform));
                    Assert.False(preset.UpdateProperties(go.transform));
                    Assert.Empty(Preset.GetAllDefaultTypes());
                    Assert.Empty(Preset.GetDefaultPresetsForType(pt));
                    Assert.Empty(Preset.GetDefaultPresetsForObject(go.transform));
                    foreach (var m in members)
                        Assert.Contains(logs.Lines, line => line.Contains("Unbound:") && line.Contains(m));
                }
            }
            finally
            {
                foreach (var m in members) Stubs.Forget(m);
                if (preset != null) Object.DestroyImmediate(preset);
                Object.DestroyImmediate(go);
            }
        }
    }
}

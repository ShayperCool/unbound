using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using Unbound.Tests;
using UnityEditor;
using UnityEditor.Presets;
using Xunit;

namespace UnityEngine.Tests
{
    public class PresetsTests : IDisposable
    {
        readonly IDisposable m_Release = TestRelease.Pin(TestRelease.Measured);

        public void Dispose() => m_Release.Dispose();

        sealed class PresetScript : MonoBehaviour
        {
            public int count = 3;
            public float speed = 1.5f;
            public string label = "default";
            public Color tint = Color.red;
            [SerializeField] int hidden = 7;
            internal int Hidden { get => hidden; set => hidden = value; }
        }

        sealed class PresetData : ScriptableObject
        {
            public int value = 4;
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
        public void PresetTypeDistinguishesValidDefaultAndExcludedClasses()
        {
            var go = new GameObject("Target");
            var script = go.AddComponent<PresetScript>();
            var excluded = go.AddComponent<ExcludedScript>();
            var box = go.AddComponent<BoxCollider>();
            var data = ScriptableObject.CreateInstance<PresetData>();
            var clip = new AnimationClip();
            var tex = new Texture2D(2, 2);
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
                Object.DestroyImmediate(tex);
                Object.DestroyImmediate(clip);
                Object.DestroyImmediate(data);
                Object.DestroyImmediate(go);
            }
        }

        [Fact]
        public void DefaultPresetConstructorsAndDefaultValueFollowUnity()
        {
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

                // Excluded property is skipped on ApplyTo and ignored by DataEquals.
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

                // Error paths.
                Assert.Throws<ArgumentNullException>(() => new Preset(null));
                Assert.Throws<NotSupportedException>(() => new Preset(srcGo));
                Assert.Throws<ArgumentNullException>(() => transformPreset.ApplyTo(null));
                Assert.Throws<ArgumentNullException>(() => Preset.GetDefaultPresetsForObject(null));
                Assert.Throws<ArgumentNullException>(() => Preset.RemoveFromDefault(null));
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
        public void UnmappedNativeTypeReportsLoudlyOnPresetCapture()
        {
            var clip = new AnimationClip();
            Preset preset = null;
            string member = "UnityEditor.Presets.Preset (serialized properties of UnityEngine.AnimationClip)";
            Stubs.Forget(member);
            try
            {
                using var logs = new LogCapture();
                preset = new Preset(clip);
                Assert.Contains(logs.Lines, line => line.Contains("Unbound:") && line.Contains(member));
            }
            finally
            {
                Stubs.Forget(member);
                if (preset != null) Object.DestroyImmediate(preset);
                Object.DestroyImmediate(clip);
            }
        }

        [Fact]
        public void UnmeasuredReleaseReportsLoudlyOnPresetType()
        {
            var go = new GameObject("Unmeasured");
            string member = "UnityEditor.Presets.PresetType.ctor";
            Stubs.Forget(member);
            try
            {
                using (TestRelease.Pin(TestRelease.Unmeasured))
                using (var logs = new LogCapture())
                {
                    var pt = new PresetType(go.transform);
                    Assert.False(pt.IsValid());
                    Assert.Contains(logs.Lines, line => line.Contains("Unbound:") && line.Contains(member));
                }
            }
            finally
            {
                Stubs.Forget(member);
                Object.DestroyImmediate(go);
            }
        }
    }
}

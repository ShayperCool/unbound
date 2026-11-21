using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using Unity.Properties;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    public class TrackPropertyValueTests
    {
        enum SparseChoice
        {
            TenStep = 10,
            [InspectorName("Mid Twenty Five")]
            TwentyFive = 25,
            HundredMax = 100
        }

        enum SignedChoice
        {
            MinusFive = -5,
            ZeroPoint = 0,
            PlusForty = 40,
            [Obsolete("Legacy")]
            OldChoice = 99
        }

        enum NonMonotonicAliasChoice
        {
            Primary = 50,
            AliasOfPrimary = 50,
            LowValue = 5
        }

        [Flags]
        enum BitFlagsChoice
        {
            None = 0,
            Read = 1 << 0,
            [InspectorName("Write Access")]
            Write = 1 << 1,
            Execute = 1 << 2,
            ReadWrite = Read | Write,
            All = ~0
        }

        [Flags]
        enum SparseBitFlags
        {
            LowBit = 1 << 0,
            HighBit = int.MinValue
        }

        [Serializable]
        struct NestedPayload
        {
            public int score;
            public string tag;
        }

        sealed class RefAsset : ScriptableObject
        {
            public int id;
        }

        sealed class TypedRefElement : VisualElement, IBindable, INotifyValueChanged<RefAsset>
        {
            RefAsset m_Value;
            public IBinding binding { get; set; }
            public string bindingPath { get; set; }

            public RefAsset value
            {
                get => m_Value;
                set
                {
                    if (EqualityComparer<RefAsset>.Default.Equals(m_Value, value)) return;
                    var prev = m_Value;
                    m_Value = value;
                    using var evt = ChangeEvent<RefAsset>.GetPooled(prev, m_Value);
                    evt.target = this;
                    SendEvent(evt);
                }
            }

            public void SetValueWithoutNotify(RefAsset newValue) => m_Value = newValue;
        }

        sealed class TestTarget : ScriptableObject
        {
            public bool flag = true;
            public int count = 10;
            public int other = 1;
            public string label = "alpha";
            public Vector2 pos2 = new Vector2(4, 5);
            public Vector3 pos = new Vector3(1, 2, 3);
            public Vector4 pos4 = new Vector4(4, 5, 6, 7);
            public Vector2Int gridPos = new Vector2Int(15, -24);
            public Vector3Int voxelPos = new Vector3Int(15, -24, 37);
            public Color tint = new Color(0.25f, 0.5f, 0.75f, 0.5f);
            [ColorUsage(false, true)]
            public Color hdrTint = new Color(2f, 4f, 0.5f, 0.25f);
            public Color32 tint32 = new Color32(16, 128, 240, 200);
            public Rect rectVal = new Rect(1, 2, 3, 4);
            public RectInt rectIntVal = new RectInt(1, 2, 3, 4);
            public Bounds boundsVal = new Bounds(Vector3.one, Vector3.one * 2f);
            public BoundsInt boundsIntVal = new BoundsInt(Vector3Int.one, Vector3Int.one * 2);
            public AnimationCurve curveVal = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            public Gradient gradientVal = new Gradient();
            [GradientUsage(true, ColorSpace.Linear)]
            public Gradient hdrGradientVal = new Gradient();
            public RefAsset refAssetVal;
            public Texture2D textureVal;
            public Transform transformVal;
            public Hash128 hashVal = Hash128.Parse("0123456789abcdef1122334455667788");
            public Hash128 otherHashVal = Hash128.Parse("fedcba98765432108877665544332211");
            public LayerMask layerMaskVal = 1;
            public NestedPayload nested = new NestedPayload { score = 5, tag = "n0" };
            public string[] items = new[] { "x", "y" };
            public SparseChoice sparseEnum = SparseChoice.TwentyFive;
            public SignedChoice signedEnum = SignedChoice.MinusFive;
            public NonMonotonicAliasChoice aliasEnum = NonMonotonicAliasChoice.Primary;
            public BitFlagsChoice flagsEnum = BitFlagsChoice.Read;
            public SparseBitFlags sparseFlagsEnum = SparseBitFlags.HighBit;
        }

        sealed class OtherTarget : ScriptableObject
        {
            public int count = 200;
            public int onlyOther = 300;
        }

        sealed class RuntimeModel
        {
            public string text = "initial";
        }

        static void Tick(BaseRuntimePanel panel)
        {
            panel.Update();
            panel.Repaint(null);
        }

        [Fact]
        public void CallbackExceptionDoesNotPreventLaterUpdatesAndSelfUnbindInsideCallbackIsSafe()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var target = ScriptableObject.CreateInstance<TestTarget>();
            using var panel = RuntimePanel.Create(settings);
            using var so = new SerializedObject(target);
            try
            {
                var el = new VisualElement();
                panel.visualTree.Add(el);
                var log = new List<string>();
                bool shouldThrow = true;
                el.TrackPropertyValue(so.FindProperty("count"), p =>
                {
                    log.Add("cb1:" + p.intValue);
                    if (shouldThrow) throw new InvalidOperationException("boom");
                });
                el.TrackPropertyValue(so.FindProperty("count"), p =>
                {
                    log.Add("cb2:" + p.intValue);
                    el.Unbind();
                });

                Tick(panel);
                Assert.Empty(log);

                // First change throws in cb1 (logged by SerializedObjectBindingContext),
                // then clears modified properties cleanly so subsequent edits still work.
                target.count = 20;
                Tick(panel);
                Assert.Equal(new[] { "cb1:20" }, log);

                // Second change runs cb1 and cb2; cb2 unbinds el mid-notification cleanly.
                shouldThrow = false;
                target.count = 30;
                Tick(panel);
                Assert.Equal(new[] { "cb1:20", "cb1:30", "cb2:30" }, log);

                // After self-unbind, further edits must not fire.
                target.count = 40;
                Tick(panel);
                Assert.Equal(3, log.Count);
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void ShrinkingArrayPastTrackedElementDoesNotThrowAndResumesWhenElementReturns()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var target = ScriptableObject.CreateInstance<TestTarget>();
            using var panel = RuntimePanel.Create(settings);
            using var so = new SerializedObject(target);
            try
            {
                var el = new VisualElement();
                panel.visualTree.Add(el);
                var log = new List<string>();
                el.TrackPropertyValue(so.FindProperty("items.Array.data[1]"), p => log.Add("i1:" + p.stringValue));
                Tick(panel);
                Assert.Empty(log);

                // Shrink array so data[1] no longer exists; poll must tolerate the missing path.
                target.items = new[] { "only" };
                Tick(panel);
                Assert.Empty(log);

                // Restore array with a different value at data[1]; tracking fires cleanly.
                target.items = new[] { "only", "restored" };
                Tick(panel);
                Assert.Equal(new[] { "i1:restored" }, log);
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void TrackingWorksOnUnmeasuredReleaseAndPreservesRuntimeDataBindingOnUnbind()
        {
            using var _ = TestRelease.Pin(TestRelease.Unmeasured);
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var target = ScriptableObject.CreateInstance<TestTarget>();
            using var panel = RuntimePanel.Create(settings);
            using var so = new SerializedObject(target);
            try
            {
                var model = new RuntimeModel();
                var label = new Label("start") { dataSource = model };
                label.SetBinding("text", new DataBinding
                {
                    dataSourcePath = new PropertyPath(nameof(RuntimeModel.text)),
                    bindingMode = BindingMode.ToTarget,
                });
                panel.visualTree.Add(label);

                var events = new List<string>();
                label.TrackPropertyValue(so.FindProperty("label"), p => events.Add(p.stringValue));
                Tick(panel);
                Assert.Equal("initial", label.text);
                Assert.Empty(events);

                // Unrelated property edit stays silent.
                target.other = 99;
                Tick(panel);
                Assert.Empty(events);

                // Tracked property and runtime binding both update.
                model.text = "next";
                target.label = "beta";
                Tick(panel);
                Assert.Equal("next", label.text);
                Assert.Equal(new[] { "beta" }, events);

                // Unbind stops SerializedProperty tracking but keeps runtime DataBinding alive.
                label.Unbind();
                model.text = "after-unbind";
                target.label = "gamma";
                Tick(panel);
                Assert.Equal("after-unbind", label.text);
                Assert.Equal(new[] { "beta" }, events);
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void DetachedRequestCancelledByUnbindAndDisposedTrackerThrowsOnPoll()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var target = ScriptableObject.CreateInstance<TestTarget>();
            using var panel = RuntimePanel.Create(settings);
            using var so = new SerializedObject(target);
            try
            {
                var el = new VisualElement();
                int count = 0;
                el.TrackPropertyValue(so.FindProperty("count"), _ => count++);
                el.Unbind();
                panel.visualTree.Add(el);
                Tick(panel);
                target.count = 123;
                Tick(panel);
                Assert.Equal(0, count);

                var tracker = new SerializedObjectChangeTracker(so);
                tracker.Dispose();
                Assert.Throws<ArgumentNullException>(() => tracker.PollForChanges(false));
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void UnbindInsideFirstCallbackSkipsRemainingCallbacksOnSameElementAndAllowsRebind()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var targetA = ScriptableObject.CreateInstance<TestTarget>();
            var targetB = ScriptableObject.CreateInstance<TestTarget>();
            using var panel = RuntimePanel.Create(settings);
            using var soA = new SerializedObject(targetA);
            using var soB = new SerializedObject(targetB);
            try
            {
                var el = new VisualElement();
                panel.visualTree.Add(el);
                var log = new List<string>();
                el.TrackPropertyValue(soA.FindProperty("count"), p =>
                {
                    log.Add("first:" + p.intValue);
                    el.Unbind();
                });
                el.TrackPropertyValue(soA.FindProperty("count"), p => log.Add("second:" + p.intValue));

                Tick(panel);
                targetA.count = 55;
                Tick(panel);
                Assert.Equal(new[] { "first:55" }, log);

                // Rebind immediately to a second SerializedObject after unbinding mid-callback.
                el.TrackPropertyValue(soB.FindProperty("count"), p => log.Add("soB:" + p.intValue));
                Tick(panel);
                targetB.count = 77;
                Tick(panel);
                Assert.Equal(new[] { "first:55", "soB:77" }, log);
            }
            finally
            {
                Object.DestroyImmediate(targetA);
                Object.DestroyImmediate(targetB);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void SerializedPropertyChangeEventTypeIdIsStableAndDistinct()
        {
            long typeId = SerializedPropertyChangeEvent.TypeId();
            Assert.Equal(typeId, SerializedPropertyChangeEvent.TypeId());
            using var fresh = new SerializedPropertyChangeEvent();
            using var pooled = SerializedPropertyChangeEvent.GetPooled(null);
            Assert.Equal(typeId, fresh.eventTypeId);
            Assert.Equal(typeId, pooled.eventTypeId);
            Assert.NotEqual(typeId, AttachToPanelEvent.TypeId());
            Assert.NotEqual(typeId, DetachFromPanelEvent.TypeId());
            Assert.NotEqual(typeId, ClickEvent.TypeId());
        }

        [Fact]
        public void SerializedObjectChangeEventTypeIdIsStableAndDistinct()
        {
            long typeId = SerializedObjectChangeEvent.TypeId();
            Assert.Equal(typeId, SerializedObjectChangeEvent.TypeId());
            using var fresh = new SerializedObjectChangeEvent();
            using var pooled = SerializedObjectChangeEvent.GetPooled(null);
            Assert.Equal(typeId, fresh.eventTypeId);
            Assert.Equal(typeId, pooled.eventTypeId);
            Assert.NotEqual(typeId, SerializedPropertyChangeEvent.TypeId());
            Assert.NotEqual(typeId, AttachToPanelEvent.TypeId());
            Assert.NotEqual(typeId, DetachFromPanelEvent.TypeId());
            Assert.NotEqual(typeId, ClickEvent.TypeId());
        }

        [Fact]
        public void TrackSerializedObjectValueRecoversFromExceptionPreservesRuntimeDataBindingAndWorksOnUnmeasuredRelease()
        {
            using var _ = TestRelease.Pin(TestRelease.Unmeasured);
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var targetA = ScriptableObject.CreateInstance<TestTarget>();
            var targetB = ScriptableObject.CreateInstance<TestTarget>();
            using var panel = RuntimePanel.Create(settings);
            using var soA = new SerializedObject(targetA);
            using var soB = new SerializedObject(targetB);
            try
            {
                var model = new RuntimeModel();
                var label = new Label("start") { dataSource = model };
                label.SetBinding("text", new DataBinding
                {
                    dataSourcePath = new PropertyPath(nameof(RuntimeModel.text)),
                    bindingMode = BindingMode.ToTarget,
                });
                panel.visualTree.Add(label);

                var log = new List<string>();
                bool shouldThrow = true;
                label.TrackSerializedObjectValue(soA, s =>
                {
                    log.Add("a:" + s.FindProperty("other").intValue);
                    if (shouldThrow) throw new InvalidOperationException("boom");
                });
                Tick(panel);
                Assert.Equal("initial", label.text);
                Assert.Empty(log);

                // First change throws inside callback; revision still advances and later edits work.
                targetA.other = 5;
                Tick(panel);
                Assert.Equal(new[] { "a:5" }, log);

                shouldThrow = false;
                model.text = "updated";
                targetA.other = 6;
                Tick(panel);
                Assert.Equal("updated", label.text);
                Assert.Equal(new[] { "a:5", "a:6" }, log);

                // Unbind stops object tracking, preserves runtime DataBinding, and allows rebind to soB.
                label.Unbind();
                model.text = "kept";
                targetA.other = 7;
                Tick(panel);
                Assert.Equal("kept", label.text);
                Assert.Equal(2, log.Count);

                label.TrackSerializedObjectValue(soB, s => log.Add("b:" + s.FindProperty("count").intValue));
                Tick(panel);
                targetB.count = 88;
                Tick(panel);
                Assert.Equal(new[] { "a:5", "a:6", "b:88" }, log);
            }
            finally
            {
                Object.DestroyImmediate(targetA);
                Object.DestroyImmediate(targetB);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void DetachedTrackSerializedObjectValueCancelledByUnbindNeverFires()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var target = ScriptableObject.CreateInstance<TestTarget>();
            using var panel = RuntimePanel.Create(settings);
            using var so = new SerializedObject(target);
            try
            {
                var el = new VisualElement();
                int count = 0;
                el.TrackSerializedObjectValue(so, _ => count++);
                el.Unbind();
                panel.visualTree.Add(el);
                Tick(panel);
                target.other = 42;
                Tick(panel);
                Assert.Equal(0, count);
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void BindPropertyScalarLifecycleAndUnmeasuredRelease()
        {
            using var _ = TestRelease.Pin(TestRelease.Unmeasured);
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var targetA = ScriptableObject.CreateInstance<TestTarget>();
            var targetB = ScriptableObject.CreateInstance<TestTarget>();
            targetA.flag = true;
            targetA.count = 15;
            targetB.flag = false;
            targetB.count = 25;
            using var panel = RuntimePanel.Create(settings);
            using var soMulti = new SerializedObject(new Object[] { targetA, targetB });
            try
            {
                var toggle = new Toggle();
                var intField = new IntegerField { bindingPath = "count" };
                panel.visualTree.Add(toggle);
                panel.visualTree.Add(intField);
                int toggleEvts = 0;
                int intEvts = 0;
                toggle.RegisterValueChangedCallback(_ => toggleEvts++);
                intField.RegisterValueChangedCallback(_ => intEvts++);

                toggle.BindProperty(soMulti.FindProperty("flag"));
                Assert.NotNull(intField.BindProperty(soMulti));
                Assert.True(toggle.showMixedValue);
                Assert.True(intField.showMixedValue);
                Assert.Equal(0, toggleEvts);
                Assert.Equal(0, intEvts);

                // Writing primary value resolves mixed state across all targets.
                intField.value = 15;
                Assert.Equal(15, targetA.count);
                Assert.Equal(15, targetB.count);
                Assert.False(intField.showMixedValue);
                Assert.Equal(1, intEvts);

                // Unrelated edit does not notify.
                targetA.other = 99;
                Tick(panel);
                Tick(panel);
                Assert.Equal(1, intEvts);

                // Unbind stops further sync.
                intField.Unbind();
                targetA.count = 77;
                targetB.count = 77;
                Tick(panel);
                Tick(panel);
                Assert.Equal(15, intField.value);
            }
            finally
            {
                Object.DestroyImmediate(targetA);
                Object.DestroyImmediate(targetB);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void BindPropertySoleFieldDetachAndReattachCatchesUp()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var target = ScriptableObject.CreateInstance<TestTarget>();
            using var panel = RuntimePanel.Create(settings);
            using var so = new SerializedObject(target);
            try
            {
                var field = new IntegerField();
                panel.visualTree.Add(field);
                field.BindProperty(so.FindProperty("count"));
                Tick(panel);
                Tick(panel);
                Assert.Equal(10, field.value);

                // Sole bound field on so: detached edits are ignored until reattach + update.
                field.RemoveFromHierarchy();
                target.count = 64;
                Tick(panel);
                Tick(panel);
                Assert.Equal(10, field.value);

                panel.visualTree.Add(field);
                Tick(panel);
                Tick(panel);
                Assert.Equal(64, field.value);
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void BindPropertyDisposedSerializedObjectUnbindsCleanlyOnEdit()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var target = ScriptableObject.CreateInstance<TestTarget>();
            using var panel = RuntimePanel.Create(settings);
            var so = new SerializedObject(target);
            try
            {
                var field = new IntegerField();
                panel.visualTree.Add(field);
                field.BindProperty(so.FindProperty("count"));
                Assert.True(field.HasBinding(BindingExtensions.s_SerializedBindingId));

                so.Dispose();
                field.value = 123;
                Assert.False(field.HasBinding(BindingExtensions.s_SerializedBindingId));
                Assert.Equal(10, target.count);
            }
            finally
            {
                so.Dispose();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void BindPropertyUnsupportedPropertyTypeReportsStubAndIncompatibleFieldWarns()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var target = ScriptableObject.CreateInstance<TestTarget>();
            using var panel = RuntimePanel.Create(settings);
            using var so = new SerializedObject(target);
            try
            {
                // Incompatible scalar field type on bool property logs Unity's warning and stays unbound.
                var wrongField = new IntegerField();
                panel.visualTree.Add(wrongField);
                var warnings = new List<string>();
                Application.LogCallback cb = (msg, _, type) =>
                {
                    if (type == LogType.Warning) warnings.Add(msg);
                };
                Application.logMessageReceived += cb;
                try
                {
                    wrongField.BindProperty(so.FindProperty("flag"));
                }
                finally
                {
                    Application.logMessageReceived -= cb;
                }
                Assert.False(wrongField.HasBinding(BindingExtensions.s_SerializedBindingId));
                Assert.Contains(warnings, w => w.Contains("not compatible") && w.Contains("\"flag\""));

                // Unsupported property type (LayerMask) reports explicit Stubs.NotImplemented hit and stays unbound.
                const string vecStubKey = "UnityEditor.UIElements.BindingExtensions.BindProperty (LayerMask)";
                int beforeVecHits = Stubs.HitReport().FirstOrDefault(h => h.Key == vecStubKey).Value;
                var vecField = new IntegerField();
                panel.visualTree.Add(vecField);
                vecField.BindProperty(so.FindProperty("layerMaskVal"));
                Assert.False(vecField.HasBinding(BindingExtensions.s_SerializedBindingId));
                Assert.True(Stubs.HitReport().FirstOrDefault(h => h.Key == vecStubKey).Value > beforeVecHits);

                // Non-string-to-string conversion reports explicit Stubs.NotImplemented hit and stays unbound.
                const string convStubKey = "UnityEditor.UIElements.Bindings.SerializedObjectStringConversionBinding<int>";
                int beforeConvHits = Stubs.HitReport().FirstOrDefault(h => h.Key == convStubKey).Value;
                var intAsText = new TextField();
                panel.visualTree.Add(intAsText);
                intAsText.BindProperty(so.FindProperty("count"));
                Assert.False(intAsText.HasBinding(BindingExtensions.s_SerializedBindingId));
                Assert.True(Stubs.HitReport().FirstOrDefault(h => h.Key == convStubKey).Value > beforeConvHits);
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void BindTreeNestedPathsCoexistWithRuntimeDataBindingAndWorkOnUnmeasuredRelease()
        {
            using var _ = TestRelease.Pin(TestRelease.Unmeasured);
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var target = ScriptableObject.CreateInstance<TestTarget>();
            using var panel = RuntimePanel.Create(settings);
            using var so = new SerializedObject(target);
            try
            {
                var model = new RuntimeModel();
                var tree = new VisualElement { dataSource = model };
                var nestedBox = new BindableElement { bindingPath = "nested" };
                var scoreField = new IntegerField { bindingPath = "score" };
                var rootFallback = new IntegerField { bindingPath = "count" };
                var missingBox = new BindableElement { bindingPath = "noSuchContainer" };
                var keepOuter = new TextField { bindingPath = "tag" };
                var rtLabel = new Label("start");
                rtLabel.SetBinding("text", new DataBinding
                {
                    dataSourcePath = new PropertyPath(nameof(RuntimeModel.text)),
                    bindingMode = BindingMode.ToTarget,
                });
                missingBox.Add(keepOuter);
                nestedBox.Add(scoreField);
                nestedBox.Add(rootFallback);
                nestedBox.Add(missingBox);
                tree.Add(nestedBox);
                tree.Add(rtLabel);
                panel.visualTree.Add(tree);

                tree.Bind(so);
                Tick(panel);
                Tick(panel);
                Assert.Equal(5, scoreField.value);
                Assert.Equal(10, rootFallback.value);
                Assert.Equal("n0", keepOuter.value);
                Assert.Equal("initial", rtLabel.text);
                Assert.False(nestedBox.HasBinding(BindingExtensions.s_SerializedBindingId));

                scoreField.value = 18;
                keepOuter.value = "n1";
                Assert.Equal(18, target.nested.score);
                Assert.Equal("n1", target.nested.tag);

                // Unbind stops SerializedObject bindings while preserving runtime DataBinding.
                tree.Unbind();
                target.nested.score = 99;
                model.text = "kept";
                Tick(panel);
                Tick(panel);
                Assert.Equal(18, scoreField.value);
                Assert.Equal("kept", rtLabel.text);
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void BindTreeDetachedLateChildAndCancelBeforeAttach()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var target = ScriptableObject.CreateInstance<TestTarget>();
            using var panel = RuntimePanel.Create(settings);
            using var so = new SerializedObject(target);
            try
            {
                var detTree = new VisualElement();
                var beforeField = new IntegerField { bindingPath = "count" };
                detTree.Add(beforeField);
                detTree.Bind(so);

                // Added after detached Bind() but before attaching to panel: bound on attach.
                var lateBeforeAttach = new TextField { bindingPath = "label" };
                detTree.Add(lateBeforeAttach);
                panel.visualTree.Add(detTree);
                Tick(panel);
                Tick(panel);
                Assert.Equal(10, beforeField.value);
                Assert.Equal("alpha", lateBeforeAttach.value);

                // Added after attached Bind(): stays unbound until Bind() is called again.
                var lateAfterAttach = new IntegerField { bindingPath = "other" };
                detTree.Add(lateAfterAttach);
                Tick(panel);
                Assert.Equal(0, lateAfterAttach.value);
                Assert.False(lateAfterAttach.HasBinding(BindingExtensions.s_SerializedBindingId));

                detTree.Bind(so);
                Tick(panel);
                Assert.Equal(1, lateAfterAttach.value);
                Assert.True(lateAfterAttach.HasBinding(BindingExtensions.s_SerializedBindingId));

                // Detached Bind() cancelled by Unbind() before attach never binds.
                var cancelTree = new VisualElement();
                var cancelField = new IntegerField { bindingPath = "count" };
                cancelTree.Add(cancelField);
                cancelTree.Bind(so);
                cancelTree.Unbind();
                panel.visualTree.Add(cancelTree);
                Tick(panel);
                Assert.Equal(0, cancelField.value);
                Assert.False(cancelField.HasBinding(BindingExtensions.s_SerializedBindingId));
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void BindTreeExplicitBindPropertyIndependenceAndDisposedSo()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var mainTarget = ScriptableObject.CreateInstance<TestTarget>();
            var otherTarget = ScriptableObject.CreateInstance<OtherTarget>();
            using var panel = RuntimePanel.Create(settings);
            var soMain = new SerializedObject(mainTarget);
            using var soOther = new SerializedObject(otherTarget);
            try
            {
                var tree = new VisualElement();
                var mainField = new IntegerField { bindingPath = "count" };
                var explicitOther = new IntegerField();
                var explicitSame = new IntegerField();
                tree.Add(mainField);
                tree.Add(explicitOther);
                tree.Add(explicitSame);
                panel.visualTree.Add(tree);

                explicitOther.BindProperty(soOther.FindProperty("onlyOther"));
                tree.Bind(soMain);
                explicitSame.BindProperty(soMain.FindProperty("other"));
                Tick(panel);
                Tick(panel);
                Assert.Equal(10, mainField.value);
                Assert.Equal(300, explicitOther.value);
                Assert.Equal(1, explicitSame.value);

                // Tree Unbind unbinds same-SO fields and stops at child with different SO context.
                tree.Unbind();
                mainTarget.count = 55;
                mainTarget.other = 66;
                otherTarget.onlyOther = 404;
                Tick(panel);
                Tick(panel);
                Assert.Equal(10, mainField.value);
                Assert.Equal(1, explicitSame.value);
                Assert.Equal(404, explicitOther.value);

                // Rebind tree to soMain and dispose soMain: update cleanly unbinds without throwing.
                tree.Bind(soMain);
                Tick(panel);
                Assert.True(mainField.HasBinding(BindingExtensions.s_SerializedBindingId));
                soMain.Dispose();
                Tick(panel);
                Tick(panel);
                Assert.False(mainField.HasBinding(BindingExtensions.s_SerializedBindingId));
            }
            finally
            {
                soMain.Dispose();
                Object.DestroyImmediate(mainTarget);
                Object.DestroyImmediate(otherTarget);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void PropertyFieldScalarLifecycleDynamicLabelAndUnmeasuredRelease()
        {
            using var _ = TestRelease.Pin(TestRelease.Unmeasured);
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var target = ScriptableObject.CreateInstance<TestTarget>();
            using var panel = RuntimePanel.Create(settings);
            using var so = new SerializedObject(target);
            try
            {
                var pf = new PropertyField(so.FindProperty("count"), "Initial");
                var events = new List<int>();
                pf.RegisterValueChangeCallback(e => events.Add(e.changedProperty.intValue));
                panel.visualTree.Add(pf);

                pf.Bind(so);
                Tick(panel);
                Tick(panel);
                var intField = Assert.IsType<IntegerField>(pf.hierarchy[0]);
                Assert.Equal("Initial", intField.label);
                Assert.Equal(10, intField.value);
                Assert.Equal(new[] { 10 }, events);

                // Dynamic label update reuses child field and updates label.
                pf.label = "Renamed";
                Tick(panel);
                Assert.Same(intField, pf.hierarchy[0]);
                Assert.Equal("Renamed", intField.label);

                // Two-way sync.
                intField.value = 33;
                Assert.Equal(33, target.count);
                target.count = 44;
                Tick(panel);
                Tick(panel);
                Assert.Equal(44, intField.value);
                Assert.Equal(44, events[^1]);
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void PropertyFieldRebindUnbindAndDisposedSoAreClean()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var target = ScriptableObject.CreateInstance<TestTarget>();
            using var panel = RuntimePanel.Create(settings);
            var so = new SerializedObject(target);
            try
            {
                var pf = new PropertyField(so.FindProperty("count"));
                panel.visualTree.Add(pf);
                pf.Bind(so);
                Tick(panel);
                var origInt = Assert.IsType<IntegerField>(pf.hierarchy[0]);
                Assert.Equal(10, origInt.value);

                // Rebind to bool replaces child field with Toggle.
                pf.BindProperty(so.FindProperty("flag"));
                Tick(panel);
                var toggle = Assert.IsType<Toggle>(pf.hierarchy[0]);
                Assert.Equal(1, pf.hierarchy.childCount);
                Assert.True(toggle.value);

                // Dispose SO -> editing label or ticking does not throw and cleans up binding.
                so.Dispose();
                pf.label = "AfterDispose";
                Tick(panel);
                Tick(panel);
                Assert.False(toggle.HasBinding(BindingExtensions.s_SerializedBindingId));
            }
            finally
            {
                so.Dispose();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void PropertyFieldUnsupportedPropertyTypeReportsStubAndUxmlDataDeserializes()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var target = ScriptableObject.CreateInstance<TestTarget>();
            using var panel = RuntimePanel.Create(settings);
            using var so = new SerializedObject(target);
            try
            {
                const string vecStubKey = "UnityEditor.UIElements.PropertyField (LayerMask)";
                int beforeHits = Stubs.HitReport().FirstOrDefault(h => h.Key == vecStubKey).Value;
                var pf = new PropertyField(so.FindProperty("layerMaskVal"));
                panel.visualTree.Add(pf);
                pf.Bind(so);
                Assert.Equal(0, pf.hierarchy.childCount);
                Assert.True(Stubs.HitReport().FirstOrDefault(h => h.Key == vecStubKey).Value > beforeHits);

                // UxmlSerializedData CreateInstance produces a valid PropertyField with ussClassName.
                var data = new PropertyField.UxmlSerializedData();
                var created = Assert.IsType<PropertyField>(data.CreateInstance());
                Assert.True(created.ClassListContains(PropertyField.ussClassName));
                data.Deserialize(created);
                Assert.Null(created.label);
                Assert.Null(created.bindingPath);
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void EnumBindingSparseNegativeAndUnmeasuredRelease()
        {
            using var _ = TestRelease.Pin(TestRelease.Unmeasured);
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var target = ScriptableObject.CreateInstance<TestTarget>();
            using var panel = RuntimePanel.Create(settings);
            using var so = new SerializedObject(target);
            try
            {
                // Direct EnumField on sparse/nonzero enum: both directions + rebind to another enum type + Unbind.
                var ef = new EnumField();
                panel.visualTree.Add(ef);
                ef.BindProperty(so.FindProperty("sparseEnum"));
                Tick(panel);
                Tick(panel);
                Assert.Equal((Enum)SparseChoice.TwentyFive, ef.value);
                Assert.Equal("Mid Twenty Five", ef.text);

                ef.value = SparseChoice.HundredMax;
                Assert.Equal(SparseChoice.HundredMax, target.sparseEnum);
                target.sparseEnum = SparseChoice.TenStep;
                Tick(panel);
                Tick(panel);
                Assert.Equal((Enum)SparseChoice.TenStep, ef.value);
                Assert.Equal("Ten Step", ef.text);

                ef.BindProperty(so.FindProperty("signedEnum"));
                Tick(panel);
                Assert.Equal((Enum)SignedChoice.MinusFive, ef.value);
                Assert.Equal("Minus Five", ef.text);

                ef.Unbind();
                Assert.Null(ef.value);
                Assert.Equal(string.Empty, ef.text);

                // PropertyField generates PopupField<string> with obsolete entries excluded.
                var pf = new PropertyField(so.FindProperty("signedEnum"));
                panel.visualTree.Add(pf);
                pf.Bind(so);
                Tick(panel);
                Tick(panel);
                var popup = Assert.IsType<PopupField<string>>(pf.hierarchy[0]);
                Assert.Equal(new[] { "Minus Five", "Zero Point", "Plus Forty" }, popup.choices);
                Assert.Equal(0, popup.index);
                Assert.Equal("Minus Five", popup.text);
                popup.index = 2;
                Assert.Equal(SignedChoice.PlusForty, target.signedEnum);

                // Direct PopupField<string> restores original choices and index on Unbind.
                var origChoices = new List<string> { "KeepA", "KeepB" };
                var directPopup = new PopupField<string>(origChoices, 1) { bindingPath = "sparseEnum" };
                panel.visualTree.Add(directPopup);
                directPopup.Bind(so);
                Tick(panel);
                Assert.Equal(new[] { "Ten Step", "Mid Twenty Five", "Hundred Max" }, directPopup.choices);
                Assert.Equal(0, directPopup.index);
                directPopup.Unbind();
                Assert.Equal(origChoices, directPopup.choices);
                Assert.Equal(1, directPopup.index);

                // Non-monotonic / aliased enum: SerializedProperty orders by (int value, ordinal name)
                // while PropertyField keeps declaration order and maps display indices accurately.
                var pfAlias = new PropertyField(so.FindProperty("aliasEnum"));
                panel.visualTree.Add(pfAlias);
                pfAlias.Bind(so);
                Tick(panel);
                var aliasPopup = Assert.IsType<PopupField<string>>(pfAlias.hierarchy[0]);
                Assert.Equal(new[] { "Primary", "Alias Of Primary", "Low Value" }, aliasPopup.choices);
                Assert.Equal(0, aliasPopup.index);
                Assert.Equal("Primary", aliasPopup.value);
                aliasPopup.index = 2;
                Assert.Equal(NonMonotonicAliasChoice.LowValue, target.aliasEnum);
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void EnumBindingMixedStateAndDisposedSoAreClean()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var t1 = ScriptableObject.CreateInstance<TestTarget>();
            var t2 = ScriptableObject.CreateInstance<TestTarget>();
            t1.sparseEnum = SparseChoice.TenStep;
            t2.sparseEnum = SparseChoice.HundredMax;
            using var panel = RuntimePanel.Create(settings);
            var soMulti = new SerializedObject(new Object[] { t1, t2 });
            try
            {
                var ef = new EnumField();
                panel.visualTree.Add(ef);
                ef.BindProperty(soMulti.FindProperty("sparseEnum"));
                Tick(panel);
                Tick(panel);
                Assert.True(ef.showMixedValue);
                Assert.Equal(BaseField<Enum>.mixedValueString, ef.text);

                // Writing target[0]'s existing value resolves mixed state across both targets.
                ef.value = SparseChoice.TenStep;
                Tick(panel);
                Assert.False(ef.showMixedValue);
                Assert.Equal("Ten Step", ef.text);
                Assert.Equal(SparseChoice.TenStep, t1.sparseEnum);
                Assert.Equal(SparseChoice.TenStep, t2.sparseEnum);

                // Disposing SO cleanly unbinds on tick without throwing.
                soMulti.Dispose();
                Tick(panel);
                Tick(panel);
                Assert.False(ef.HasBinding(BindingExtensions.s_SerializedBindingId));
            }
            finally
            {
                soMulti.Dispose();
                Object.DestroyImmediate(t1);
                Object.DestroyImmediate(t2);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void EnumBindingFlagsAndScalarControlsBindEnum()
        {
            using var _ = TestRelease.Pin(TestRelease.Unmeasured);
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var target = ScriptableObject.CreateInstance<TestTarget>();
            using var panel = RuntimePanel.Create(settings);
            using var so = new SerializedObject(target);
            try
            {
                // PropertyField generates EnumFlagsField for [Flags] enum and binds two-way.
                var pfFlags = new PropertyField(so.FindProperty("flagsEnum"), "Permissions");
                panel.visualTree.Add(pfFlags);
                pfFlags.Bind(so);
                Tick(panel);
                Tick(panel);
                var eff = Assert.IsType<EnumFlagsField>(pfFlags.hierarchy[0]);
                Assert.Equal("Permissions", eff.label);
                Assert.Equal((Enum)BitFlagsChoice.Read, eff.value);
                Assert.Equal("Read", eff.text);

                // All defined bits normalize to -1 (All) and write back to SerializedObject.
                eff.value = BitFlagsChoice.Read | BitFlagsChoice.Write | BitFlagsChoice.Execute;
                Assert.Equal(BitFlagsChoice.All, target.flagsEnum);
                Assert.Equal("All", eff.text);

                // Direct EnumFlagsField on signed high-bit / no-zero/no-all enum strips undefined bits.
                var effSparse = new EnumFlagsField();
                panel.visualTree.Add(effSparse);
                effSparse.BindProperty(so.FindProperty("sparseFlagsEnum"));
                Tick(panel);
                Tick(panel);
                Assert.Equal((Enum)SparseBitFlags.HighBit, effSparse.value);
                Assert.Equal("High Bit", effSparse.text);
                effSparse.value = (SparseBitFlags)(1 << 8); // undefined bit strips to 0
                Assert.Equal((SparseBitFlags)0, target.sparseFlagsEnum);
                Assert.Equal("Nothing", effSparse.text);

                // Menu toggle updates value and unbinding resets to zero without disturbing other bindings.
                effSparse.ChangeValueFromMenu("Low Bit");
                Assert.Equal(SparseBitFlags.LowBit, target.sparseFlagsEnum);
                effSparse.Unbind();
                Assert.Equal((Enum)(SparseBitFlags)0, effSparse.value);
                Assert.Equal("Nothing", effSparse.text);

                // UxmlSerializedData CreateInstance produces a valid EnumFlagsField.
                var uxmlData = new EnumFlagsField.UxmlSerializedData();
                var created = Assert.IsType<EnumFlagsField>(uxmlData.CreateInstance());
                Assert.True(created.ClassListContains(EnumFlagsField.ussClassName));
                uxmlData.Deserialize(created);
                Assert.Null(created.value);

                // IntegerField and TextField bind ordinary enums via int value and display name string.
                var intField = new IntegerField();
                var textField = new TextField();
                panel.visualTree.Add(intField);
                panel.visualTree.Add(textField);
                intField.BindProperty(so.FindProperty("sparseEnum"));
                textField.BindProperty(so.FindProperty("sparseEnum"));
                Tick(panel);
                Tick(panel);
                Assert.Equal(25, intField.value);
                Assert.Equal("Mid Twenty Five", textField.value);
                intField.value = 100;
                Assert.Equal(SparseChoice.HundredMax, target.sparseEnum);
                textField.value = "Ten Step";
                Assert.Equal(SparseChoice.TenStep, target.sparseEnum);
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void EnumFlagsMixedMultiObjectAndDisposedSoAreClean()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var t1 = ScriptableObject.CreateInstance<TestTarget>();
            var t2 = ScriptableObject.CreateInstance<TestTarget>();
            t1.flagsEnum = BitFlagsChoice.Read;
            t2.flagsEnum = BitFlagsChoice.Execute;
            using var panel = RuntimePanel.Create(settings);
            var soMulti = new SerializedObject(new Object[] { t1, t2 });
            try
            {
                var eff = new EnumFlagsField();
                panel.visualTree.Add(eff);
                eff.BindProperty(soMulti.FindProperty("flagsEnum"));
                Tick(panel);
                Tick(panel);
                Assert.True(eff.showMixedValue);
                Assert.Equal(BaseField<Enum>.mixedValueString, eff.text);

                // Writing target[0]'s existing value resolves mixed state across both targets.
                eff.value = BitFlagsChoice.Read;
                Tick(panel);
                Assert.False(eff.showMixedValue);
                Assert.Equal("Read", eff.text);
                Assert.Equal(BitFlagsChoice.Read, t1.flagsEnum);
                Assert.Equal(BitFlagsChoice.Read, t2.flagsEnum);

                // Disposed SO unbinds cleanly on update.
                soMulti.Dispose();
                Tick(panel);
                Tick(panel);
                Assert.False(eff.HasBinding(BindingExtensions.s_SerializedBindingId));
            }
            finally
            {
                soMulti.Dispose();
                Object.DestroyImmediate(t1);
                Object.DestroyImmediate(t2);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void Vector3BindingComponentsTransformMultiEditAndUnmeasuredRelease()
        {
            using var _ = TestRelease.Pin(TestRelease.Unmeasured);
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var target = ScriptableObject.CreateInstance<TestTarget>();
            var goA = new GameObject("VecGoA");
            var goB = new GameObject("VecGoB");
            goA.transform.localPosition = new Vector3(1f, 10f, 100f);
            goB.transform.localPosition = new Vector3(2f, 10f, 200f);
            using var panel = RuntimePanel.Create(settings);
            using var so = new SerializedObject(target);
            using var soTransform = new SerializedObject(new Object[] { goA.transform, goB.transform });
            try
            {
                // Vector3Field + PropertyField work under an unmeasured release including sub-field edits and NaN tolerance.
                var pf = new PropertyField(so.FindProperty("pos"));
                var pfEvents = new List<Vector3>();
                pf.RegisterValueChangeCallback(e => pfEvents.Add(e.changedProperty.vector3Value));
                panel.visualTree.Add(pf);
                pf.Bind(so);
                Tick(panel);
                Tick(panel);

                var vf = Assert.IsType<Vector3Field>(pf.hierarchy[0]);
                Assert.Equal(new Vector3(1, 2, 3), vf.value);
                Assert.Equal(new[] { new Vector3(1, 2, 3) }, pfEvents);

                vf.Q<FloatField>("unity-y-input").value = -12.5f;
                Assert.Equal(new Vector3(1f, -12.5f, 3f), target.pos);
                Assert.Equal(2, pfEvents.Count);

                // NaN component values round-trip cleanly without throwing.
                so.FindProperty("pos").vector3Value = new Vector3(float.NaN, -0f, 4.5f);
                so.ApplyModifiedProperties();
                Tick(panel);
                Tick(panel);
                Assert.True(float.IsNaN(vf.value.x));
                Assert.Equal(4.5f, vf.value.z);

                // Native Transform.m_LocalPosition multi-object edit preserves untouched components on each target.
                var tfField = new Vector3Field();
                panel.visualTree.Add(tfField);
                tfField.BindProperty(soTransform.FindProperty("m_LocalPosition"));
                Tick(panel);
                Tick(panel);
                Assert.False(tfField.showMixedValue);
                Assert.True(tfField.Q<FloatField>("unity-x-input").showMixedValue);
                Assert.False(tfField.Q<FloatField>("unity-y-input").showMixedValue);
                Assert.True(tfField.Q<FloatField>("unity-z-input").showMixedValue);

                tfField.Q<FloatField>("unity-x-input").value = 9f;
                Assert.Equal(new Vector3(9f, 10f, 100f), goA.transform.localPosition);
                Assert.Equal(new Vector3(9f, 10f, 200f), goB.transform.localPosition);
                Assert.False(tfField.Q<FloatField>("unity-x-input").showMixedValue);
                Assert.True(tfField.Q<FloatField>("unity-z-input").showMixedValue);
            }
            finally
            {
                Object.DestroyImmediate(goA);
                Object.DestroyImmediate(goB);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void Vector3MixedMultiObjectPerComponentPreservationAndDisposedSo()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var t1 = ScriptableObject.CreateInstance<TestTarget>();
            var t2 = ScriptableObject.CreateInstance<TestTarget>();
            t1.pos = new Vector3(1f, 10f, 100f);
            t2.pos = new Vector3(2f, 10f, 200f);
            using var panel = RuntimePanel.Create(settings);
            var soMulti = new SerializedObject(new Object[] { t1, t2 });
            try
            {
                var vf = new Vector3Field();
                var compX = new FloatField();
                panel.visualTree.Add(vf);
                panel.visualTree.Add(compX);
                vf.BindProperty(soMulti.FindProperty("pos"));
                compX.BindProperty(soMulti.FindProperty("pos.x"));
                Tick(panel);
                Tick(panel);

                var xSub = vf.Q<FloatField>("unity-x-input");
                var ySub = vf.Q<FloatField>("unity-y-input");
                var zSub = vf.Q<FloatField>("unity-z-input");
                Assert.False(vf.showMixedValue);
                Assert.True(xSub.showMixedValue);
                Assert.False(ySub.showMixedValue);
                Assert.True(zSub.showMixedValue);
                Assert.True(compX.showMixedValue);

                // Editing a single component updates both targets while leaving untouched mixed axes intact.
                ySub.value = 15f;
                Tick(panel);
                Assert.Equal(new Vector3(1f, 15f, 100f), t1.pos);
                Assert.Equal(new Vector3(2f, 15f, 200f), t2.pos);
                Assert.True(xSub.showMixedValue);
                Assert.False(ySub.showMixedValue);
                Assert.True(zSub.showMixedValue);

                // Direct component binding reflects external SO update via component contentHash.
                soMulti.FindProperty("pos.x").floatValue = 55f;
                soMulti.ApplyModifiedProperties();
                Tick(panel);
                Tick(panel);
                Assert.Equal(55f, compX.value);
                Assert.False(compX.showMixedValue);
                Assert.False(xSub.showMixedValue);
                Assert.True(zSub.showMixedValue);
                Assert.Equal(new Vector3(55f, 15f, 100f), t1.pos);
                Assert.Equal(new Vector3(55f, 15f, 200f), t2.pos);

                // Disposed SO unbinds cleanly on update without throwing.
                soMulti.Dispose();
                Tick(panel);
                Tick(panel);
                Assert.False(vf.HasBinding(BindingExtensions.s_SerializedBindingId));
                Assert.False(compX.HasBinding(BindingExtensions.s_SerializedBindingId));
            }
            finally
            {
                soMulti.Dispose();
                Object.DestroyImmediate(t1);
                Object.DestroyImmediate(t2);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void Vector2BindingMixedPerComponentUnmeasuredAndDisposedSo()
        {
            using var _ = TestRelease.Pin(TestRelease.Unmeasured);
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var t1 = ScriptableObject.CreateInstance<TestTarget>();
            var t2 = ScriptableObject.CreateInstance<TestTarget>();
            t1.pos2 = new Vector2(1f, 100f);
            t2.pos2 = new Vector2(2f, 200f);
            using var panel = RuntimePanel.Create(settings);
            var soMulti = new SerializedObject(new Object[] { t1, t2 });
            using var soSingle = new SerializedObject(t1);
            try
            {
                // PropertyField + Vector2Field on mixed multi-object targets preserve untouched component.
                var pf = new PropertyField(soMulti.FindProperty("pos2"));
                var compY = new FloatField();
                panel.visualTree.Add(pf);
                panel.visualTree.Add(compY);
                pf.Bind(soMulti);
                compY.BindProperty(soMulti.FindProperty("pos2.y"));
                Tick(panel);
                Tick(panel);

                var vf = Assert.IsType<Vector2Field>(pf.hierarchy[0]);
                var xSub = vf.Q<FloatField>("unity-x-input");
                var ySub = vf.Q<FloatField>("unity-y-input");
                Assert.False(vf.showMixedValue);
                Assert.True(xSub.showMixedValue);
                Assert.True(ySub.showMixedValue);
                Assert.True(compY.showMixedValue);

                xSub.value = 9f;
                Tick(panel);
                Assert.Equal(new Vector2(9f, 100f), t1.pos2);
                Assert.Equal(new Vector2(9f, 200f), t2.pos2);
                Assert.False(xSub.showMixedValue);
                Assert.True(ySub.showMixedValue);

                // External SO component edit updates both direct FloatField and Vector2Field.
                soMulti.FindProperty("pos2.y").floatValue = 350f;
                soMulti.ApplyModifiedProperties();
                Tick(panel);
                Tick(panel);
                Assert.Equal(350f, compY.value);
                Assert.False(compY.showMixedValue);
                Assert.False(ySub.showMixedValue);
                Assert.Equal(new Vector2(9f, 350f), t1.pos2);
                Assert.Equal(new Vector2(9f, 350f), t2.pos2);

                // NaN tolerance on single-target binding.
                var singleField = new Vector2Field();
                panel.visualTree.Add(singleField);
                singleField.BindProperty(soSingle.FindProperty("pos2"));
                Tick(panel);
                soSingle.FindProperty("pos2").vector2Value = new Vector2(float.NaN, -0f);
                soSingle.ApplyModifiedProperties();
                Tick(panel);
                Tick(panel);
                Assert.True(float.IsNaN(singleField.value.x));

                // Disposed SO unbinds cleanly without throwing.
                soMulti.Dispose();
                Tick(panel);
                Tick(panel);
                Assert.False(vf.HasBinding(BindingExtensions.s_SerializedBindingId));
                Assert.False(compY.HasBinding(BindingExtensions.s_SerializedBindingId));
            }
            finally
            {
                soMulti.Dispose();
                Object.DestroyImmediate(t1);
                Object.DestroyImmediate(t2);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void Vector4BindingUnmeasuredPreservesWAndDisposedSo()
        {
            using var _ = TestRelease.Pin(TestRelease.Unmeasured);
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var t1 = ScriptableObject.CreateInstance<TestTarget>();
            var t2 = ScriptableObject.CreateInstance<TestTarget>();
            t1.pos4 = new Vector4(1f, 10f, 100f, 1000f);
            t2.pos4 = new Vector4(2f, 10f, 200f, 2000f);
            using var panel = RuntimePanel.Create(settings);
            var soMulti = new SerializedObject(new Object[] { t1, t2 });
            using var soSingle = new SerializedObject(t1);
            try
            {
                // On unmeasured releases, editing x on mixed Vector4 with z != w preserves both z and w cleanly.
                var vf = new Vector4Field();
                panel.visualTree.Add(vf);
                vf.BindProperty(soMulti.FindProperty("pos4"));
                Tick(panel);
                Tick(panel);

                var xSub = vf.Q<FloatField>("unity-x-input");
                var ySub = vf.Q<FloatField>("unity-y-input");
                var zSub = vf.Q<FloatField>("unity-z-input");
                var wSub = vf.Q<FloatField>("unity-w-input");
                Assert.False(vf.showMixedValue);
                Assert.True(xSub.showMixedValue);
                Assert.False(ySub.showMixedValue);
                Assert.True(zSub.showMixedValue);
                Assert.True(wSub.showMixedValue);

                xSub.value = 6f;
                Tick(panel);
                Assert.Equal(new Vector4(6f, 10f, 100f, 1000f), t1.pos4);
                Assert.Equal(new Vector4(6f, 10f, 200f, 2000f), t2.pos4);
                Assert.False(xSub.showMixedValue);
                Assert.True(zSub.showMixedValue);
                Assert.True(wSub.showMixedValue);

                // Editing w to equal target[0].z writes w cleanly on unmeasured releases.
                wSub.value = 100f;
                Tick(panel);
                Assert.Equal(new Vector4(6f, 10f, 100f, 100f), t1.pos4);
                Assert.Equal(new Vector4(6f, 10f, 200f, 100f), t2.pos4);
                Assert.False(wSub.showMixedValue);

                // NaN tolerance on single-target Vector4Field.
                var singleField = new Vector4Field();
                panel.visualTree.Add(singleField);
                singleField.BindProperty(soSingle.FindProperty("pos4"));
                Tick(panel);
                soSingle.FindProperty("pos4").vector4Value = new Vector4(float.NaN, -0f, 3f, 4f);
                soSingle.ApplyModifiedProperties();
                Tick(panel);
                Tick(panel);
                Assert.True(float.IsNaN(singleField.value.x));
                Assert.Equal(4f, singleField.value.w);

                // Disposed SO unbinds cleanly without throwing.
                soMulti.Dispose();
                Tick(panel);
                Tick(panel);
                Assert.False(vf.HasBinding(BindingExtensions.s_SerializedBindingId));
            }
            finally
            {
                soMulti.Dispose();
                Object.DestroyImmediate(t1);
                Object.DestroyImmediate(t2);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void Vector4BindingMeasured6000_0MultiEditAndPropertyFieldFoldout()
        {
            using var _ = TestRelease.Pin(TestRelease.Measured);
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var t1 = ScriptableObject.CreateInstance<TestTarget>();
            var t2 = ScriptableObject.CreateInstance<TestTarget>();
            t1.pos4 = new Vector4(1f, 10f, 100f, 1000f);
            t2.pos4 = new Vector4(2f, 10f, 200f, 2000f);
            using var panel = RuntimePanel.Create(settings);
            using var soMulti = new SerializedObject(new Object[] { t1, t2 });
            try
            {
                // PropertyField for Vector4 creates a Foldout bound to isExpanded with lazy x/y/z/w FloatFields.
                var pf = new PropertyField(soMulti.FindProperty("pos4"), "Bounds Plane");
                panel.visualTree.Add(pf);
                pf.Bind(soMulti);
                Tick(panel);
                Tick(panel);

                var foldout = Assert.IsType<Foldout>(pf.hierarchy[0]);
                Assert.False(foldout.value);
                Assert.Equal("Bounds Plane", foldout.text);
                Assert.Empty(foldout.Query<PropertyField>().ToList());

                foldout.value = true;
                Tick(panel);
                Assert.True(soMulti.FindProperty("pos4").isExpanded);
                Assert.Equal(4, foldout.Query<PropertyField>().ToList().Count);
                Assert.True(pf.Q<FloatField>("unity-input-pos4.w").showMixedValue);

                // Editing child PropertyField's x FloatField preserves z and w even when z != w.
                pf.Q<FloatField>("unity-input-pos4.x").value = 8f;
                Assert.Equal(new Vector4(8f, 10f, 100f, 1000f), t1.pos4);
                Assert.Equal(new Vector4(8f, 10f, 200f, 2000f), t2.pos4);
                Assert.True(pf.Q<FloatField>("unity-input-pos4.w").showMixedValue);

                // Re-adding an existing child to the same parent and rebinding an expanded Foldout PropertyField do not throw.
                var container = new VisualElement();
                var childA = new VisualElement { name = "a" };
                var childB = new VisualElement { name = "b" };
                container.Add(childA);
                container.Add(childB);
                container.Add(childA);
                Assert.Equal(2, container.childCount);
                Assert.Same(childB, container[0]);
                Assert.Same(childA, container[1]);

                pf.label = "Updated Plane";
                Tick(panel);
                Assert.Same(foldout, pf.hierarchy[0]);
                Assert.Equal("Updated Plane", foldout.text);
                Assert.Equal(4, foldout.Query<PropertyField>().ToList().Count);

                // Direct Vector4Field on 6000.0 matches the measured z-vs-w comparison quirk.
                var vf = new Vector4Field();
                panel.visualTree.Add(vf);
                vf.BindProperty(soMulti.FindProperty("pos4"));
                Tick(panel);
                vf.Q<FloatField>("unity-x-input").value = 9f;
                Assert.Equal(new Vector4(9f, 10f, 100f, 1000f), t1.pos4);
                Assert.Equal(new Vector4(9f, 10f, 200f, 1000f), t2.pos4);
            }
            finally
            {
                Object.DestroyImmediate(t1);
                Object.DestroyImmediate(t2);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void BindPropertyVector2IntMultiEditAndDisposedCleanupWorksAcrossReleases()
        {
            using var _ = TestRelease.Pin(TestRelease.Unmeasured);
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var t1 = ScriptableObject.CreateInstance<TestTarget>();
            var t2 = ScriptableObject.CreateInstance<TestTarget>();
            t1.gridPos = new Vector2Int(int.MinValue, 100);
            t2.gridPos = new Vector2Int(int.MaxValue, 200);
            using var panel = RuntimePanel.Create(settings);
            var soMulti = new SerializedObject(new Object[] { t1, t2 });
            try
            {
                var vf = new Vector2IntField();
                panel.visualTree.Add(vf);
                vf.BindProperty(soMulti.FindProperty("gridPos"));
                Tick(panel);

                var xSub = vf.Q<IntegerField>("unity-x-input");
                var ySub = vf.Q<IntegerField>("unity-y-input");
                Assert.False(vf.showMixedValue);
                Assert.True(xSub.showMixedValue);
                Assert.True(ySub.showMixedValue);
                Assert.Equal(int.MinValue, vf.value.x);
                Assert.Equal(100, vf.value.y);

                // Editing X via sub-field updates both targets' X while preserving distinct Y values.
                xSub.value = -500;
                Tick(panel);
                Assert.Equal(new Vector2Int(-500, 100), t1.gridPos);
                Assert.Equal(new Vector2Int(-500, 200), t2.gridPos);
                Assert.False(xSub.showMixedValue);
                Assert.True(ySub.showMixedValue);

                // PropertyField creates Vector2IntField and preserves untouched components on multi-edit.
                var pf = new PropertyField(soMulti.FindProperty("gridPos"));
                panel.visualTree.Add(pf);
                pf.Bind(soMulti);
                Tick(panel);
                var pfChild = Assert.IsType<Vector2IntField>(pf.hierarchy[0]);
                Assert.Equal("Grid Pos", pfChild.label);
                Assert.False(pfChild.Q<IntegerField>("unity-x-input").showMixedValue);
                Assert.True(pfChild.Q<IntegerField>("unity-y-input").showMixedValue);

                // Reset to partial mixed (X mixed, Y equal) and edit only Y via whole-vector value.
                t1.gridPos = new Vector2Int(1, 10);
                t2.gridPos = new Vector2Int(2, 10);
                soMulti.Update();
                Tick(panel);
                pfChild.value = new Vector2Int(1, int.MaxValue);
                Tick(panel);
                Assert.Equal(new Vector2Int(1, int.MaxValue), t1.gridPos);
                Assert.Equal(new Vector2Int(2, int.MaxValue), t2.gridPos);
                Assert.True(pfChild.Q<IntegerField>("unity-x-input").showMixedValue);
                Assert.False(pfChild.Q<IntegerField>("unity-y-input").showMixedValue);

                // Direct IntegerField bound to gridPos.x only touches X.
                var compX = new IntegerField();
                panel.visualTree.Add(compX);
                compX.BindProperty(soMulti.FindProperty("gridPos.x"));
                Tick(panel);
                Assert.True(compX.showMixedValue);
                compX.value = int.MinValue;
                Tick(panel);
                Assert.Equal(new Vector2Int(int.MinValue, int.MaxValue), t1.gridPos);
                Assert.Equal(new Vector2Int(int.MinValue, int.MaxValue), t2.gridPos);

                // Disposing SerializedObject causes subsequent UI edits to unbind cleanly without throwing.
                soMulti.Dispose();
                vf.value = new Vector2Int(9, 8);
                Tick(panel);
                Assert.Equal(new Vector2Int(int.MinValue, int.MaxValue), t1.gridPos);
            }
            finally
            {
                soMulti.Dispose();
                Object.DestroyImmediate(t1);
                Object.DestroyImmediate(t2);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void BindPropertyVector3IntMultiEditAndDisposedCleanupWorksAcrossReleases()
        {
            using var _ = TestRelease.Pin(TestRelease.Unmeasured);
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var t1 = ScriptableObject.CreateInstance<TestTarget>();
            var t2 = ScriptableObject.CreateInstance<TestTarget>();
            t1.voxelPos = new Vector3Int(int.MinValue, 100, -10);
            t2.voxelPos = new Vector3Int(int.MaxValue, 200, -20);
            using var panel = RuntimePanel.Create(settings);
            var soMulti = new SerializedObject(new Object[] { t1, t2 });
            try
            {
                var rt = new RuntimeModel { text = "rt-v3i-0" };
                var vf = new Vector3IntField { dataSource = rt };
                vf.SetBinding("tooltip", new DataBinding
                {
                    dataSourcePath = new PropertyPath(nameof(RuntimeModel.text)),
                    bindingMode = BindingMode.ToTarget
                });
                panel.visualTree.Add(vf);
                vf.BindProperty(soMulti.FindProperty("voxelPos"));
                Tick(panel);

                var xSub = vf.Q<IntegerField>("unity-x-input");
                var ySub = vf.Q<IntegerField>("unity-y-input");
                var zSub = vf.Q<IntegerField>("unity-z-input");
                Assert.False(vf.showMixedValue);
                Assert.True(xSub.showMixedValue);
                Assert.True(ySub.showMixedValue);
                Assert.True(zSub.showMixedValue);
                Assert.Equal(new Vector3Int(int.MinValue, 100, -10), vf.value);
                Assert.Equal("rt-v3i-0", vf.tooltip);

                // Editing X via sub-field updates both targets' X while preserving distinct Y and Z values.
                xSub.value = -500;
                Tick(panel);
                Assert.Equal(new Vector3Int(-500, 100, -10), t1.voxelPos);
                Assert.Equal(new Vector3Int(-500, 200, -20), t2.voxelPos);
                Assert.False(xSub.showMixedValue);
                Assert.True(ySub.showMixedValue);
                Assert.True(zSub.showMixedValue);

                // PropertyField creates Vector3IntField and preserves untouched components on multi-edit.
                var pf = new PropertyField(soMulti.FindProperty("voxelPos"));
                panel.visualTree.Add(pf);
                pf.Bind(soMulti);
                Tick(panel);
                var pfChild = Assert.IsType<Vector3IntField>(pf.hierarchy[0]);
                Assert.Equal("Voxel Pos", pfChild.label);
                Assert.False(pfChild.Q<IntegerField>("unity-x-input").showMixedValue);
                Assert.True(pfChild.Q<IntegerField>("unity-y-input").showMixedValue);
                Assert.True(pfChild.Q<IntegerField>("unity-z-input").showMixedValue);

                // Reset to partial mixed (X and Z mixed, Y equal) and edit only Y via whole-vector value.
                t1.voxelPos = new Vector3Int(1, 10, -5);
                t2.voxelPos = new Vector3Int(2, 10, -6);
                soMulti.Update();
                Tick(panel);
                pfChild.value = new Vector3Int(1, int.MaxValue, -5);
                Tick(panel);
                Assert.Equal(new Vector3Int(1, int.MaxValue, -5), t1.voxelPos);
                Assert.Equal(new Vector3Int(2, int.MaxValue, -6), t2.voxelPos);
                Assert.True(pfChild.Q<IntegerField>("unity-x-input").showMixedValue);
                Assert.False(pfChild.Q<IntegerField>("unity-y-input").showMixedValue);
                Assert.True(pfChild.Q<IntegerField>("unity-z-input").showMixedValue);

                // Direct IntegerField bound to voxelPos.z only touches Z and preserves X and Y.
                var compZ = new IntegerField();
                panel.visualTree.Add(compZ);
                compZ.BindProperty(soMulti.FindProperty("voxelPos.z"));
                Tick(panel);
                Assert.True(compZ.showMixedValue);
                compZ.value = int.MinValue;
                Tick(panel);
                Assert.Equal(new Vector3Int(1, int.MaxValue, int.MinValue), t1.voxelPos);
                Assert.Equal(new Vector3Int(2, int.MaxValue, int.MinValue), t2.voxelPos);

                // Disposing SerializedObject causes subsequent UI edits to unbind cleanly while runtime DataBinding stays active.
                soMulti.Dispose();
                vf.value = new Vector3Int(9, 8, 7);
                rt.text = "rt-v3i-1";
                Tick(panel);
                Assert.Equal(new Vector3Int(1, int.MaxValue, int.MinValue), t1.voxelPos);
                Assert.Equal("rt-v3i-1", vf.tooltip);
            }
            finally
            {
                soMulti.Dispose();
                Object.DestroyImmediate(t1);
                Object.DestroyImmediate(t2);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void BindPropertyColorAndColorUsageWorkAcrossReleases()
        {
            using var _ = TestRelease.Pin(TestRelease.Unmeasured);
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var t1 = ScriptableObject.CreateInstance<TestTarget>();
            var t2 = ScriptableObject.CreateInstance<TestTarget>();
            t1.tint = new Color(0.25f, 0.5f, 0.75f, 0.25f);
            t2.tint = new Color(0.25f, 0.5f, 0.75f, 0.75f);
            t1.hdrTint = new Color(3f, 1.5f, 0.25f, 0.5f);
            t2.hdrTint = new Color(5f, 1.5f, 0.25f, 0.5f);
            using var panel = RuntimePanel.Create(settings);
            var soMulti = new SerializedObject(new Object[] { t1, t2 });
            try
            {
                // Standalone defaults match Unity 6000.0 (showAlpha=true, hdr=false, showEyeDropper=false).
                var standalone = new ColorField();
                Assert.Equal(default(Color), standalone.value);
                Assert.True(standalone.showAlpha);
                Assert.False(standalone.hdr);
                Assert.False(standalone.showEyeDropper);

                // UXML traits default show-eye-dropper to true when absent.
#pragma warning disable CS0618
                var traits = new ColorField.UxmlTraits();
                traits.Init(standalone, new EmptyUxmlAttributes(), default);
#pragma warning restore CS0618
                Assert.True(standalone.showEyeDropper);

                // Alpha-only divergence shows mixed state; writing target[0]'s value resolves both targets.
                var rt = new RuntimeModel { text = "rt-color-0" };
                var cf = new ColorField { dataSource = rt };
                cf.SetBinding("tooltip", new DataBinding
                {
                    dataSourcePath = new PropertyPath(nameof(RuntimeModel.text)),
                    bindingMode = BindingMode.ToTarget
                });
                panel.visualTree.Add(cf);
                cf.BindProperty(soMulti.FindProperty("tint"));
                Tick(panel);
                Assert.True(cf.showMixedValue);
                Assert.Equal(t1.tint, cf.value);
                Assert.Equal("rt-color-0", cf.tooltip);

                cf.value = t1.tint;
                Tick(panel);
                Assert.False(cf.showMixedValue);
                Assert.Equal(t1.tint, t2.tint);

                // PropertyField applies [ColorUsage(false, true)] to generated ColorField and preserves HDR + alpha values.
                var pfUsage = new PropertyField(soMulti.FindProperty("hdrTint"));
                panel.visualTree.Add(pfUsage);
                pfUsage.Bind(soMulti);
                Tick(panel);
                var usageField = Assert.IsType<ColorField>(pfUsage.hierarchy[0]);
                Assert.Equal("", usageField.name);
                Assert.Equal("Hdr Tint", usageField.label);
                Assert.False(usageField.showAlpha);
                Assert.True(usageField.hdr);
                Assert.True(usageField.showMixedValue);
                usageField.value = new Color(6f, 2f, 0.5f, 0.25f);
                Tick(panel);
                Assert.Equal(new Color(6f, 2f, 0.5f, 0.25f), t1.hdrTint);
                Assert.Equal(new Color(6f, 2f, 0.5f, 0.25f), t2.hdrTint);
                Assert.False(usageField.showMixedValue);

                // Color32 binding quantizes cleanly through ColorField.
                var cf32 = new ColorField();
                panel.visualTree.Add(cf32);
                cf32.BindProperty(soMulti.FindProperty("tint32"));
                Tick(panel);
                cf32.value = new Color(1f, 0f, 0.5f, 0.25f);
                Tick(panel);
                Assert.Equal(new Color32(255, 0, 128, 64), t1.tint32);
                Assert.Equal(new Color32(255, 0, 128, 64), t2.tint32);

                // Disposed SerializedObject unbinds cleanly while runtime DataBinding stays active.
                soMulti.Dispose();
                cf.value = Color.white;
                rt.text = "rt-color-1";
                Tick(panel);
                Assert.Equal(new Color(0.25f, 0.5f, 0.75f, 0.25f), t1.tint);
                Assert.Equal("rt-color-1", cf.tooltip);
            }
            finally
            {
                soMulti.Dispose();
                Object.DestroyImmediate(t1);
                Object.DestroyImmediate(t2);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void BindPropertyRectMultiEditComponentBindingAndDisposedCleanupWorkAcrossReleases()
        {
            using var _ = TestRelease.Pin(TestRelease.Unmeasured);
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var t1 = ScriptableObject.CreateInstance<TestTarget>();
            var t2 = ScriptableObject.CreateInstance<TestTarget>();
            t1.rectVal = new Rect(1f, 10f, -100f, 50f);
            t2.rectVal = new Rect(2f, 10f, -200f, 50f);
            using var panel = RuntimePanel.Create(settings);
            var soMulti = new SerializedObject(new Object[] { t1, t2 });
            try
            {
                var rt = new RuntimeModel { text = "rt-rect-0" };
                var rf = new RectField { dataSource = rt };
                rf.SetBinding("tooltip", new DataBinding
                {
                    dataSourcePath = new PropertyPath(nameof(RuntimeModel.text)),
                    bindingMode = BindingMode.ToTarget
                });
                panel.visualTree.Add(rf);
                rf.BindProperty(soMulti.FindProperty("rectVal"));
                Tick(panel);

                var xSub = rf.Q<FloatField>("unity-x-input");
                var ySub = rf.Q<FloatField>("unity-y-input");
                var wSub = rf.Q<FloatField>("unity-width-input");
                var hSub = rf.Q<FloatField>("unity-height-input");
                Assert.False(rf.showMixedValue);
                Assert.True(xSub.showMixedValue);
                Assert.False(ySub.showMixedValue);
                Assert.True(wSub.showMixedValue);
                Assert.False(hSub.showMixedValue);
                Assert.Equal(new Rect(1f, 10f, -100f, 50f), rf.value);
                Assert.Equal("rt-rect-0", rf.tooltip);

                // Editing a sub-field on RectField writes the whole Rect across targets (matching Unity 6000.0.71f1).
                xSub.value = 5f;
                Tick(panel);
                Assert.Equal(new Rect(5f, 10f, -100f, 50f), t1.rectVal);
                Assert.Equal(new Rect(5f, 10f, -100f, 50f), t2.rectVal);
                Assert.False(xSub.showMixedValue);
                Assert.False(wSub.showMixedValue);

                // PropertyField creates RectField with nicified label and per-component showMixedValue.
                t1.rectVal = new Rect(10f, 20f, 30f, 40f);
                t2.rectVal = new Rect(50f, 60f, 70f, 80f);
                soMulti.Update();
                var pf = new PropertyField(soMulti.FindProperty("rectVal"));
                panel.visualTree.Add(pf);
                pf.Bind(soMulti);
                Tick(panel);
                var pfChild = Assert.IsType<RectField>(pf.hierarchy[0]);
                Assert.Equal("Rect Val", pfChild.label);
                Assert.True(pfChild.Q<FloatField>("unity-width-input").showMixedValue);

                // Direct FloatField bound to rectVal.width updates only width and preserves x, y, and height on each target.
                var compW = new FloatField();
                panel.visualTree.Add(compW);
                compW.BindProperty(soMulti.FindProperty("rectVal.width"));
                Tick(panel);
                Assert.True(compW.showMixedValue);
                compW.value = 99f;
                Tick(panel);
                Assert.Equal(new Rect(10f, 20f, 99f, 40f), t1.rectVal);
                Assert.Equal(new Rect(50f, 60f, 99f, 80f), t2.rectVal);

                // Disposing SerializedObject unbinds cleanly while runtime DataBinding remains active.
                soMulti.Dispose();
                rf.value = new Rect(9f, 8f, 7f, 6f);
                rt.text = "rt-rect-1";
                Tick(panel);
                Assert.Equal(new Rect(10f, 20f, 99f, 40f), t1.rectVal);
                Assert.Equal("rt-rect-1", rf.tooltip);
            }
            finally
            {
                soMulti.Dispose();
                Object.DestroyImmediate(t1);
                Object.DestroyImmediate(t2);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void BindPropertyRectIntMultiEditComponentBindingAndDisposedCleanupWorkAcrossReleases()
        {
            using var _ = TestRelease.Pin(TestRelease.Unmeasured);
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var t1 = ScriptableObject.CreateInstance<TestTarget>();
            var t2 = ScriptableObject.CreateInstance<TestTarget>();
            t1.rectIntVal = new RectInt(int.MinValue, 10, -100, int.MaxValue);
            t2.rectIntVal = new RectInt(2, 10, -200, int.MaxValue);
            using var panel = RuntimePanel.Create(settings);
            var soMulti = new SerializedObject(new Object[] { t1, t2 });
            try
            {
                var rt = new RuntimeModel { text = "rt-rectint-0" };
                var rif = new RectIntField { dataSource = rt };
                rif.SetBinding("tooltip", new DataBinding
                {
                    dataSourcePath = new PropertyPath(nameof(RuntimeModel.text)),
                    bindingMode = BindingMode.ToTarget
                });
                panel.visualTree.Add(rif);
                rif.BindProperty(soMulti.FindProperty("rectIntVal"));
                Tick(panel);

                var xSub = rif.Q<IntegerField>("unity-x-input");
                var ySub = rif.Q<IntegerField>("unity-y-input");
                var wSub = rif.Q<IntegerField>("unity-width-input");
                var hSub = rif.Q<IntegerField>("unity-height-input");
                Assert.False(rif.showMixedValue);
                Assert.True(xSub.showMixedValue);
                Assert.False(ySub.showMixedValue);
                Assert.True(wSub.showMixedValue);
                Assert.False(hSub.showMixedValue);
                Assert.Equal(new RectInt(int.MinValue, 10, -100, int.MaxValue), rif.value);
                Assert.Equal("rt-rectint-0", rif.tooltip);

                // Editing a sub-field on RectIntField writes the whole RectInt across targets (matching Unity 6000.0.71f1).
                xSub.value = 5;
                Tick(panel);
                Assert.Equal(new RectInt(5, 10, -100, int.MaxValue), t1.rectIntVal);
                Assert.Equal(new RectInt(5, 10, -100, int.MaxValue), t2.rectIntVal);
                Assert.False(xSub.showMixedValue);
                Assert.False(wSub.showMixedValue);

                // PropertyField creates RectIntField with nicified label and per-component showMixedValue.
                t1.rectIntVal = new RectInt(10, 20, 30, 40);
                t2.rectIntVal = new RectInt(50, 60, 70, 80);
                soMulti.Update();
                var pf = new PropertyField(soMulti.FindProperty("rectIntVal"));
                panel.visualTree.Add(pf);
                pf.Bind(soMulti);
                Tick(panel);
                var pfChild = Assert.IsType<RectIntField>(pf.hierarchy[0]);
                Assert.Equal("Rect Int Val", pfChild.label);
                Assert.True(pfChild.Q<IntegerField>("unity-width-input").showMixedValue);

                // Direct IntegerField bound to rectIntVal.width updates only width and preserves x, y, and height on each target.
                var compW = new IntegerField();
                panel.visualTree.Add(compW);
                compW.BindProperty(soMulti.FindProperty("rectIntVal.width"));
                Tick(panel);
                Assert.True(compW.showMixedValue);
                compW.value = 99;
                Tick(panel);
                Assert.Equal(new RectInt(10, 20, 99, 40), t1.rectIntVal);
                Assert.Equal(new RectInt(50, 60, 99, 80), t2.rectIntVal);

                // Disposing SerializedObject unbinds cleanly while runtime DataBinding remains active.
                soMulti.Dispose();
                rif.value = new RectInt(9, 8, 7, 6);
                rt.text = "rt-rectint-1";
                Tick(panel);
                Assert.Equal(new RectInt(10, 20, 99, 40), t1.rectIntVal);
                Assert.Equal("rt-rectint-1", rif.tooltip);
            }
            finally
            {
                soMulti.Dispose();
                Object.DestroyImmediate(t1);
                Object.DestroyImmediate(t2);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void BindPropertyBoundsMultiEditComponentBindingAndDisposedCleanupWorkAcrossReleases()
        {
            using var _ = TestRelease.Pin(TestRelease.Unmeasured);
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var t1 = ScriptableObject.CreateInstance<TestTarget>();
            var t2 = ScriptableObject.CreateInstance<TestTarget>();
            t1.boundsVal = new Bounds(new Vector3(1f, 10f, 100f), new Vector3(6f, 8f, 20f));
            t2.boundsVal = new Bounds(new Vector3(2f, 10f, 100f), new Vector3(6f, 8f, 40f));
            using var panel = RuntimePanel.Create(settings);
            var soMulti = new SerializedObject(new Object[] { t1, t2 });
            try
            {
                var rt = new RuntimeModel { text = "rt-bounds-0" };
                var bf = new BoundsField { dataSource = rt };
                bf.SetBinding("tooltip", new DataBinding
                {
                    dataSourcePath = new PropertyPath(nameof(RuntimeModel.text)),
                    bindingMode = BindingMode.ToTarget
                });
                panel.visualTree.Add(bf);
                bf.BindProperty(soMulti.FindProperty("boundsVal"));
                Tick(panel);

                var center = bf.Q<Vector3Field>("unity-m_Center-input");
                var extents = bf.Q<Vector3Field>("unity-m_Extent-input");
                var cxSub = center.Q<FloatField>("unity-x-input");
                var cySub = center.Q<FloatField>("unity-y-input");
                var ezSub = extents.Q<FloatField>("unity-z-input");
                Assert.False(bf.showMixedValue);
                Assert.False(center.showMixedValue);
                Assert.False(extents.showMixedValue);
                Assert.True(cxSub.showMixedValue);
                Assert.False(cySub.showMixedValue);
                Assert.True(ezSub.showMixedValue);
                Assert.Equal(t1.boundsVal, bf.value);
                Assert.Equal("rt-bounds-0", bf.tooltip);

                // Editing a sub-field on BoundsField writes the whole Bounds across targets (matching Unity 6000.0.71f1).
                cxSub.value = 5f;
                Tick(panel);
                var expectedWhole = new Bounds(new Vector3(5f, 10f, 100f), new Vector3(6f, 8f, 20f));
                Assert.Equal(expectedWhole, t1.boundsVal);
                Assert.Equal(expectedWhole, t2.boundsVal);
                Assert.False(cxSub.showMixedValue);
                Assert.False(ezSub.showMixedValue);

                // PropertyField creates BoundsField with nicified label and per-axis showMixedValue.
                t1.boundsVal = new Bounds(new Vector3(10f, 20f, 30f), new Vector3(2f, 4f, 6f));
                t2.boundsVal = new Bounds(new Vector3(50f, 60f, 70f), new Vector3(8f, 10f, 12f));
                soMulti.Update();
                var pf = new PropertyField(soMulti.FindProperty("boundsVal"));
                panel.visualTree.Add(pf);
                pf.Bind(soMulti);
                Tick(panel);
                var pfChild = Assert.IsType<BoundsField>(pf.hierarchy[0]);
                Assert.Equal("Bounds Val", pfChild.label);
                Assert.True(pfChild.Q<Vector3Field>("unity-m_Center-input").Q<FloatField>("unity-x-input").showMixedValue);
                Assert.True(pfChild.Q<Vector3Field>("unity-m_Extent-input").Q<FloatField>("unity-z-input").showMixedValue);

                // Direct Vector3Field bound to boundsVal.m_Center preserves untouched center axes and extents on each target.
                var compCenter = new Vector3Field();
                panel.visualTree.Add(compCenter);
                compCenter.BindProperty(soMulti.FindProperty("boundsVal.m_Center"));
                Tick(panel);
                Assert.True(compCenter.Q<FloatField>("unity-x-input").showMixedValue);
                compCenter.Q<FloatField>("unity-x-input").value = 99f;
                Tick(panel);
                Assert.Equal(new Bounds(new Vector3(99f, 20f, 30f), new Vector3(2f, 4f, 6f)), t1.boundsVal);
                Assert.Equal(new Bounds(new Vector3(99f, 60f, 70f), new Vector3(8f, 10f, 12f)), t2.boundsVal);

                // Direct FloatField bound to boundsVal.m_Extent.z updates only extents.z and preserves the rest on each target.
                var compEz = new FloatField();
                panel.visualTree.Add(compEz);
                compEz.BindProperty(soMulti.FindProperty("boundsVal.m_Extent.z"));
                Tick(panel);
                Assert.True(compEz.showMixedValue);
                compEz.value = 77f;
                Tick(panel);
                Assert.Equal(new Bounds(new Vector3(99f, 20f, 30f), new Vector3(2f, 4f, 154f)), t1.boundsVal);
                Assert.Equal(new Bounds(new Vector3(99f, 60f, 70f), new Vector3(8f, 10f, 154f)), t2.boundsVal);

                // Disposing SerializedObject unbinds cleanly while runtime DataBinding remains active.
                soMulti.Dispose();
                bf.value = new Bounds(new Vector3(9f, 8f, 7f), new Vector3(2f, 4f, 6f));
                rt.text = "rt-bounds-1";
                Tick(panel);
                Assert.Equal(new Bounds(new Vector3(99f, 20f, 30f), new Vector3(2f, 4f, 154f)), t1.boundsVal);
                Assert.Equal("rt-bounds-1", bf.tooltip);
            }
            finally
            {
                soMulti.Dispose();
                Object.DestroyImmediate(t1);
                Object.DestroyImmediate(t2);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void BindPropertyBoundsIntMultiEditComponentBindingAndDisposedCleanupWorkAcrossReleases()
        {
            using var _ = TestRelease.Pin(TestRelease.Unmeasured);
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var t1 = ScriptableObject.CreateInstance<TestTarget>();
            var t2 = ScriptableObject.CreateInstance<TestTarget>();
            t1.boundsIntVal = new BoundsInt(new Vector3Int(int.MinValue, 10, 100), new Vector3Int(-6, 8, int.MaxValue));
            t2.boundsIntVal = new BoundsInt(new Vector3Int(2, 10, 100), new Vector3Int(-6, 8, 40));
            using var panel = RuntimePanel.Create(settings);
            var soMulti = new SerializedObject(new Object[] { t1, t2 });
            try
            {
                var rt = new RuntimeModel { text = "rt-boundsint-0" };
                var bif = new BoundsIntField { dataSource = rt };
                bif.SetBinding("tooltip", new DataBinding
                {
                    dataSourcePath = new PropertyPath(nameof(RuntimeModel.text)),
                    bindingMode = BindingMode.ToTarget
                });
                panel.visualTree.Add(bif);
                bif.BindProperty(soMulti.FindProperty("boundsIntVal"));
                Tick(panel);

                var pos = bif.Q<Vector3IntField>("unity-m_Position-input");
                var size = bif.Q<Vector3IntField>("unity-m_Size-input");
                var pxSub = pos.Q<IntegerField>("unity-x-input");
                var pySub = pos.Q<IntegerField>("unity-y-input");
                var szSub = size.Q<IntegerField>("unity-z-input");
                Assert.False(bif.showMixedValue);
                Assert.False(pos.showMixedValue);
                Assert.False(size.showMixedValue);
                Assert.True(pxSub.showMixedValue);
                Assert.False(pySub.showMixedValue);
                Assert.True(szSub.showMixedValue);
                Assert.Equal(t1.boundsIntVal, bif.value);
                Assert.Equal("rt-boundsint-0", bif.tooltip);

                // Editing a sub-field on BoundsIntField writes the whole BoundsInt across targets (matching Unity 6000.0.71f1).
                pxSub.value = 5;
                Tick(panel);
                var expectedWhole = new BoundsInt(new Vector3Int(5, 10, 100), new Vector3Int(-6, 8, int.MaxValue));
                Assert.Equal(expectedWhole, t1.boundsIntVal);
                Assert.Equal(expectedWhole, t2.boundsIntVal);
                Assert.False(pxSub.showMixedValue);
                Assert.False(szSub.showMixedValue);

                // PropertyField creates BoundsIntField with nicified label and per-axis showMixedValue.
                t1.boundsIntVal = new BoundsInt(new Vector3Int(10, 20, 30), new Vector3Int(2, 4, 6));
                t2.boundsIntVal = new BoundsInt(new Vector3Int(50, 60, 70), new Vector3Int(8, 10, 12));
                soMulti.Update();
                var pf = new PropertyField(soMulti.FindProperty("boundsIntVal"));
                panel.visualTree.Add(pf);
                pf.Bind(soMulti);
                Tick(panel);
                var pfChild = Assert.IsType<BoundsIntField>(pf.hierarchy[0]);
                Assert.Equal("Bounds Int Val", pfChild.label);
                Assert.True(pfChild.Q<Vector3IntField>("unity-m_Position-input").Q<IntegerField>("unity-x-input").showMixedValue);
                Assert.True(pfChild.Q<Vector3IntField>("unity-m_Size-input").Q<IntegerField>("unity-z-input").showMixedValue);

                // Direct Vector3IntField bound to boundsIntVal.m_Position preserves untouched position axes and size on each target.
                var compPos = new Vector3IntField();
                panel.visualTree.Add(compPos);
                compPos.BindProperty(soMulti.FindProperty("boundsIntVal.m_Position"));
                Tick(panel);
                Assert.True(compPos.Q<IntegerField>("unity-x-input").showMixedValue);
                compPos.Q<IntegerField>("unity-x-input").value = 99;
                Tick(panel);
                Assert.Equal(new BoundsInt(new Vector3Int(99, 20, 30), new Vector3Int(2, 4, 6)), t1.boundsIntVal);
                Assert.Equal(new BoundsInt(new Vector3Int(99, 60, 70), new Vector3Int(8, 10, 12)), t2.boundsIntVal);

                // Direct IntegerField bound to boundsIntVal.m_Size.z updates only size.z and preserves the rest on each target.
                var compSz = new IntegerField();
                panel.visualTree.Add(compSz);
                compSz.BindProperty(soMulti.FindProperty("boundsIntVal.m_Size.z"));
                Tick(panel);
                Assert.True(compSz.showMixedValue);
                compSz.value = 77;
                Tick(panel);
                Assert.Equal(new BoundsInt(new Vector3Int(99, 20, 30), new Vector3Int(2, 4, 77)), t1.boundsIntVal);
                Assert.Equal(new BoundsInt(new Vector3Int(99, 60, 70), new Vector3Int(8, 10, 77)), t2.boundsIntVal);

                // Disposing SerializedObject unbinds cleanly while runtime DataBinding remains active.
                soMulti.Dispose();
                bif.value = new BoundsInt(new Vector3Int(9, 8, 7), new Vector3Int(2, 4, 6));
                rt.text = "rt-boundsint-1";
                Tick(panel);
                Assert.Equal(new BoundsInt(new Vector3Int(99, 20, 30), new Vector3Int(2, 4, 77)), t1.boundsIntVal);
                Assert.Equal("rt-boundsint-1", bif.tooltip);
            }
            finally
            {
                soMulti.Dispose();
                Object.DestroyImmediate(t1);
                Object.DestroyImmediate(t2);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void BindPropertyAnimationCurveCopyIndependenceMixedAndDisposedCleanupWorkAcrossReleases()
        {
            using var _ = TestRelease.Pin(TestRelease.Unmeasured);
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var t1 = ScriptableObject.CreateInstance<TestTarget>();
            var t2 = ScriptableObject.CreateInstance<TestTarget>();
            t1.curveVal = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 2f)) { preWrapMode = WrapMode.Loop, postWrapMode = WrapMode.ClampForever };
            t2.curveVal = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 2f)) { preWrapMode = WrapMode.Loop, postWrapMode = WrapMode.PingPong };
            using var panel = RuntimePanel.Create(settings);
            var soMulti = new SerializedObject(new Object[] { t1, t2 });
            try
            {
                var rt = new RuntimeModel { text = "rt-curve-0" };
                var cf = new CurveField { dataSource = rt };
                cf.SetBinding("tooltip", new DataBinding
                {
                    dataSourcePath = new PropertyPath(nameof(RuntimeModel.text)),
                    bindingMode = BindingMode.ToTarget
                });
                panel.visualTree.Add(cf);
                cf.BindProperty(soMulti.FindProperty("curveVal"));
                Tick(panel);

                // Wrap-mode-only divergence sets showMixedValue and value getter returns independent copies.
                Assert.True(cf.showMixedValue);
                Assert.NotNull(cf.Q<Label>(className: BaseField<AnimationCurve>.mixedValueLabelUssClassName));
                var readA = cf.value;
                var readB = cf.value;
                Assert.NotSame(readA, readB);
                Assert.NotSame(readA, t1.curveVal);
                readA.AddKey(0.5f, 99f);
                Assert.Equal(2, cf.value.length);
                Assert.Equal("rt-curve-0", cf.tooltip);

                // Editing mixed CurveField updates both targets with distinct AnimationCurve instances.
                var edited = new AnimationCurve(new Keyframe(0f, 5f), new Keyframe(1f, 6f)) { preWrapMode = WrapMode.PingPong, postWrapMode = WrapMode.Loop };
                cf.value = edited;
                edited.AddKey(0.5f, 123f);
                Tick(panel);
                Assert.False(cf.showMixedValue);
                Assert.NotSame(t1.curveVal, t2.curveVal);
                Assert.Equal(2, t1.curveVal.length);
                Assert.Equal(2, t2.curveVal.length);
                Assert.Equal(WrapMode.PingPong, t1.curveVal.preWrapMode);
                Assert.Equal(WrapMode.Loop, t2.curveVal.postWrapMode);

                // PropertyField creates CurveField with nicified label and propagates wrap-mode-only SO updates.
                var pf = new PropertyField(soMulti.FindProperty("curveVal"));
                panel.visualTree.Add(pf);
                pf.Bind(soMulti);
                Tick(panel);
                var pfChild = Assert.IsType<CurveField>(pf.hierarchy[0]);
                Assert.Equal("Curve Val", pfChild.label);
                var wrapUpdated = soMulti.FindProperty("curveVal").animationCurveValue;
                wrapUpdated.postWrapMode = WrapMode.ClampForever;
                soMulti.FindProperty("curveVal").animationCurveValue = wrapUpdated;
                soMulti.ApplyModifiedProperties();
                Tick(panel);
                Assert.Equal(WrapMode.ClampForever, pfChild.value.postWrapMode);

                // Same key times and wrap modes, only key value/tangent/weight changed: contentHash must differ and sync to UI.
                uint hashBefore = soMulti.FindProperty("curveVal").contentHash;
                soMulti.FindProperty("curveVal").animationCurveValue = new AnimationCurve(
                    new Keyframe(0f, 15f, 0f, 3f),
                    new Keyframe(1f, 26f, -2f, 0f, 0.25f, 0.75f))
                {
                    preWrapMode = WrapMode.PingPong,
                    postWrapMode = WrapMode.ClampForever,
                };
                soMulti.ApplyModifiedProperties();
                Assert.NotEqual(hashBefore, soMulti.FindProperty("curveVal").contentHash);
                Tick(panel);
                Assert.Equal(15f, pfChild.value[0].value);
                Assert.Equal(26f, pfChild.value[1].value);
                Assert.Equal(0.25f, pfChild.value[1].inWeight);

                // Disposing SerializedObject unbinds cleanly while runtime DataBinding remains active.
                soMulti.Dispose();
                cf.value = AnimationCurve.Linear(0f, 10f, 1f, 20f);
                rt.text = "rt-curve-1";
                Tick(panel);
                Assert.Equal(WrapMode.ClampForever, t1.curveVal.postWrapMode);
                Assert.Equal("rt-curve-1", cf.tooltip);
            }
            finally
            {
                soMulti.Dispose();
                Object.DestroyImmediate(t1);
                Object.DestroyImmediate(t2);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void BindPropertyGradientCopyIndependenceTimeEqualityMixedUsageAndDisposedCleanupWorkAcrossReleases()
        {
            static Gradient MakeGrad(Color c0, Color c1, float midTime, float a0, float a1, GradientMode mode = GradientMode.Blend)
            {
                var g = new Gradient { mode = mode };
                g.SetKeys(
                    new[] { new GradientColorKey(c0, 0f), new GradientColorKey(Color.green, midTime), new GradientColorKey(c1, 1f) },
                    new[] { new GradientAlphaKey(a0, 0f), new GradientAlphaKey(0.5f, midTime), new GradientAlphaKey(a1, 1f) });
                return g;
            }

            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var t1 = ScriptableObject.CreateInstance<TestTarget>();
            var t2 = ScriptableObject.CreateInstance<TestTarget>();
            t1.gradientVal = MakeGrad(Color.red, Color.blue, 0.5f, 1f, 0.5f, GradientMode.Blend);
            t2.gradientVal = MakeGrad(Color.red, Color.blue, 0.5f, 1f, 0.5f, GradientMode.Fixed);
            t1.hdrGradientVal = MakeGrad(new Color(2f, 0.5f, 0f, 1f), new Color(0f, 3f, 1f, 1f), 0.5f, 0.8f, 0.2f, GradientMode.PerceptualBlend);
            using var panel = RuntimePanel.Create(settings);

            // Measured 6000.0 release control: GradientField ignores key-time-only edits (Compat.GradientFieldIgnoresKeyTimeInEquality).
            using (TestRelease.Pin(TestRelease.Measured))
            {
                var gf60 = new GradientField();
                int ev60 = 0;
                gf60.RegisterValueChangedCallback(_ => ev60++);
                panel.visualTree.Add(gf60);
                gf60.value = MakeGrad(Color.red, Color.blue, 0.5f, 1f, 0f, GradientMode.Fixed);
                Assert.Equal(1, ev60);
                gf60.value = MakeGrad(Color.red, Color.blue, 0.75f, 1f, 0f, GradientMode.Fixed);
                Assert.Equal(1, ev60);
                Assert.InRange(gf60.value.colorKeys[1].time, 0.499f, 0.501f);
                gf60.RemoveFromHierarchy();
            }

            // Unmeasured release control: GradientField compares key times properly and fires change events.
            using var _ = TestRelease.Pin(TestRelease.Unmeasured);
            var soMulti = new SerializedObject(new Object[] { t1, t2 });
            var soSingle = new SerializedObject(t1);
            try
            {
                var gfTime = new GradientField();
                int evUnmeasured = 0;
                gfTime.RegisterValueChangedCallback(_ => evUnmeasured++);
                panel.visualTree.Add(gfTime);
                gfTime.value = MakeGrad(Color.red, Color.blue, 0.5f, 1f, 0f, GradientMode.Fixed);
                Assert.Equal(1, evUnmeasured);
                gfTime.value = MakeGrad(Color.red, Color.blue, 0.75f, 1f, 0f, GradientMode.Fixed);
                Assert.Equal(2, evUnmeasured);
                Assert.InRange(gfTime.value.colorKeys[1].time, 0.749f, 0.751f);
                gfTime.RemoveFromHierarchy();

                var rt = new RuntimeModel { text = "rt-grad-0" };
                var gf = new GradientField { dataSource = rt };
                gf.SetBinding("tooltip", new DataBinding
                {
                    dataSourcePath = new PropertyPath(nameof(RuntimeModel.text)),
                    bindingMode = BindingMode.ToTarget
                });
                panel.visualTree.Add(gf);
                gf.BindProperty(soMulti.FindProperty("gradientVal"));
                Tick(panel);

                // Mode-only divergence sets showMixedValue and value getter returns independent copies.
                Assert.True(gf.showMixedValue);
                Assert.NotNull(gf.Q<Label>(className: BaseField<Gradient>.mixedValueLabelUssClassName));
                var readA = gf.value;
                var readB = gf.value;
                Assert.NotSame(readA, readB);
                Assert.NotSame(readA, t1.gradientVal);
                readA.mode = GradientMode.PerceptualBlend;
                Assert.Equal(GradientMode.Blend, gf.value.mode);
                Assert.Equal("rt-grad-0", gf.tooltip);

                // Editing mixed GradientField updates both targets with distinct Gradient instances.
                var edited = MakeGrad(Color.yellow, Color.cyan, 0.25f, 0.75f, 0.25f, GradientMode.PerceptualBlend);
                gf.value = edited;
                edited.mode = GradientMode.Blend;
                Tick(panel);
                Assert.False(gf.showMixedValue);
                Assert.NotSame(t1.gradientVal, t2.gradientVal);
                Assert.Equal(GradientMode.PerceptualBlend, t1.gradientVal.mode);
                Assert.Equal(GradientMode.PerceptualBlend, t2.gradientVal.mode);
                Assert.Equal(Color.yellow, t2.gradientVal.colorKeys[0].color);

                // PropertyField creates GradientField for plain and [GradientUsage(true, ColorSpace.Linear)] properties.
                var pfPlain = new PropertyField(soMulti.FindProperty("gradientVal"));
                panel.visualTree.Add(pfPlain);
                pfPlain.Bind(soMulti);
                Tick(panel);
                var pfPlainChild = Assert.IsType<GradientField>(pfPlain.hierarchy[0]);
                Assert.Equal("Gradient Val", pfPlainChild.label);
                Assert.False(pfPlainChild.hdr);
                Assert.Equal(ColorSpace.Gamma, pfPlainChild.colorSpace);

                // Same key times and mode, only color and alpha changed: contentHash must differ and sync to UI.
                uint hashBefore = soMulti.FindProperty("gradientVal").contentHash;
                soMulti.FindProperty("gradientVal").gradientValue = MakeGrad(Color.magenta, Color.white, 0.25f, 0.25f, 0.75f, GradientMode.PerceptualBlend);
                soMulti.ApplyModifiedProperties();
                Assert.NotEqual(hashBefore, soMulti.FindProperty("gradientVal").contentHash);
                Tick(panel);
                Assert.Equal(Color.magenta, pfPlainChild.value.colorKeys[0].color);
                Assert.Equal(0.25f, pfPlainChild.value.alphaKeys[0].alpha);

                var pfUsage = new PropertyField(soSingle.FindProperty("hdrGradientVal"));
                panel.visualTree.Add(pfUsage);
                pfUsage.Bind(soSingle);
                Tick(panel);
                var pfUsageChild = Assert.IsType<GradientField>(pfUsage.hierarchy[0]);
                Assert.True(pfUsageChild.hdr);
                Assert.Equal(ColorSpace.Linear, pfUsageChild.colorSpace);
                Assert.Equal(new Color(2f, 0.5f, 0f, 1f), pfUsageChild.value.colorKeys[0].color);

                // Disposing SerializedObject unbinds cleanly while runtime DataBinding remains active.
                soMulti.Dispose();
                gf.value = MakeGrad(Color.black, Color.black, 0.5f, 1f, 1f, GradientMode.Fixed);
                rt.text = "rt-grad-1";
                Tick(panel);
                Assert.Equal(GradientMode.PerceptualBlend, t1.gradientVal.mode);
                Assert.Equal("rt-grad-1", gf.tooltip);
            }
            finally
            {
                soMulti.Dispose();
                soSingle.Dispose();
                Object.DestroyImmediate(t1);
                Object.DestroyImmediate(t2);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void BindPropertyObjectReferenceCoercionMissingMixedTypedElementsAndDisposedCleanupWorkAcrossReleases()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var t1 = ScriptableObject.CreateInstance<TestTarget>();
            var t2 = ScriptableObject.CreateInstance<TestTarget>();
            var a1 = ScriptableObject.CreateInstance<RefAsset>();
            a1.name = "FirstAsset";
            var a2 = ScriptableObject.CreateInstance<RefAsset>();
            a2.name = "SecondAsset";
            var a3 = ScriptableObject.CreateInstance<RefAsset>();
            a3.name = "ThirdAsset";
            var doomed = ScriptableObject.CreateInstance<RefAsset>();
            doomed.name = "DoomedAsset";
            var tex = new Texture2D(2, 2) { name = "IconTex" };
            var go = new GameObject("ActorHost");
            t1.refAssetVal = a1;
            t2.refAssetVal = a2;
            t1.textureVal = tex;
            t1.transformVal = go.transform;

            using var panel = RuntimePanel.Create(settings);
            using var _ = TestRelease.Pin(TestRelease.Unmeasured);
            var soMulti = new SerializedObject(new Object[] { t1, t2 });
            var soSingle = new SerializedObject(t1);
            try
            {
                // Standalone coercion, display formatting and UXML traits defaults.
                var ofStandalone = new ObjectField("Ref") { objectType = typeof(Transform) };
                Assert.True(ofStandalone.allowSceneObjects);
                Assert.Equal("None (Transform)", ofStandalone.Q<Label>(className: "unity-object-field-display__label").text);
                ofStandalone.value = go;
                Assert.Same(go.transform, ofStandalone.value);
                Assert.Equal("ActorHost (Transform)", ofStandalone.Q<Label>(className: "unity-object-field-display__label").text);
                ofStandalone.objectType = typeof(Texture2D);
                ofStandalone.value = tex;
                Assert.Equal("IconTex", ofStandalone.Q<Label>(className: "unity-object-field-display__label").text);
                ofStandalone.objectType = null;
                ofStandalone.value = null;
                Assert.Equal("<no type>", ofStandalone.Q<Label>(className: "unity-object-field-display__label").text);

                // Multi-object mixed binding + runtime DataBinding coexistence.
                var rt = new RuntimeModel { text = "rt-obj-0" };
                var ofMulti = new ObjectField { objectType = typeof(RefAsset), dataSource = rt };
                ofMulti.SetBinding("tooltip", new DataBinding
                {
                    dataSourcePath = new PropertyPath(nameof(RuntimeModel.text)),
                    bindingMode = BindingMode.ToTarget
                });
                panel.visualTree.Add(ofMulti);
                ofMulti.BindProperty(soMulti.FindProperty("refAssetVal"));
                Tick(panel);

                Assert.True(ofMulti.showMixedValue);
                var mixedLbl = ofMulti.Q<Label>(className: "unity-object-field-display__label");
                Assert.Equal(BaseField<Object>.mixedValueString, mixedLbl.text);
                Assert.True(mixedLbl.ClassListContains(BaseField<Object>.mixedValueLabelUssClassName));
                Assert.Equal("rt-obj-0", ofMulti.tooltip);

                // Editing mixed field to target[0]'s value overwrites all targets and clears mixed.
                ofMulti.value = a1;
                Tick(panel);
                Assert.False(ofMulti.showMixedValue);
                Assert.Same(a1, t1.refAssetVal);
                Assert.Same(a1, t2.refAssetVal);
                Assert.Equal("FirstAsset (Ref Asset)", mixedLbl.text);

                // Typed custom INotifyValueChanged<RefAsset> binds via DefaultBindUnityObject.
                var typedEl = new TypedRefElement();
                panel.visualTree.Add(typedEl);
                typedEl.BindProperty(soSingle.FindProperty("refAssetVal"));
                Tick(panel);
                Assert.Same(a1, typedEl.value);
                typedEl.value = a3;
                Tick(panel);
                Assert.Same(a3, t1.refAssetVal);

                // Missing/destroyed reference reports Missing via ObjectContent and clears on value = null.
                t1.refAssetVal = doomed;
                soSingle.Update();
                var ofMiss = new ObjectField { objectType = typeof(RefAsset) };
                panel.visualTree.Add(ofMiss);
                ofMiss.BindProperty(soSingle.FindProperty("refAssetVal"));
                Tick(panel);
                Assert.Same(doomed, ofMiss.value);
                Object.DestroyImmediate(doomed);
                soSingle.Update();
                var pMiss = soSingle.FindProperty("refAssetVal");
                Assert.True(ReferenceEquals(pMiss.objectReferenceValue, null));
                Assert.NotEqual(0, pMiss.objectReferenceInstanceIDValue);
                Assert.True(ObjectField.IsMissingObjectReference(pMiss));
                ofMiss.value = null;
                Tick(panel);
                soSingle.Update();
                Assert.Equal(0, soSingle.FindProperty("refAssetVal").objectReferenceInstanceIDValue);
                Assert.True(ReferenceEquals(t1.refAssetVal, null));
                Assert.Equal("None (Ref Asset)", ofMiss.Q<Label>(className: "unity-object-field-display__label").text);

                // Wrong type assigned to bound ObjectField clears target via unsafeMode and syncs back to null.
                t1.refAssetVal = a2;
                Tick(panel);
                Assert.Same(a2, ofMiss.value);
                ofMiss.value = tex;
                Tick(panel);
                Assert.Null(t1.refAssetVal);
                Assert.Null(ofMiss.value);

                // PropertyField infers objectType and reuses ObjectField across different object-reference types.
                var pf = new PropertyField(soSingle.FindProperty("refAssetVal"));
                panel.visualTree.Add(pf);
                pf.Bind(soSingle);
                Tick(panel);
                var pfOf = Assert.IsType<ObjectField>(pf.hierarchy[0]);
                Assert.Equal(typeof(RefAsset), pfOf.objectType);
                Assert.Equal("Ref Asset Val", pfOf.label);
                pf.BindProperty(soSingle.FindProperty("textureVal"));
                Tick(panel);
                Assert.Same(pfOf, pf.hierarchy[0]);
                Assert.Equal(typeof(Texture2D), pfOf.objectType);
                Assert.Same(tex, pfOf.value);
                Assert.Equal("IconTex", pfOf.Q<Label>(className: "unity-object-field-display__label").text);

                // Disposing SerializedObject unbinds cleanly while runtime DataBinding remains active.
                soMulti.Dispose();
                ofMulti.value = a3;
                rt.text = "rt-obj-1";
                Tick(panel);
                Assert.Same(a1, t2.refAssetVal);
                Assert.Equal("rt-obj-1", ofMulti.tooltip);
            }
            finally
            {
                soMulti.Dispose();
                soSingle.Dispose();
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(tex);
                Object.DestroyImmediate(doomed);
                Object.DestroyImmediate(a3);
                Object.DestroyImmediate(a2);
                Object.DestroyImmediate(a1);
                Object.DestroyImmediate(t1);
                Object.DestroyImmediate(t2);
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void BindPropertyHash128MixedSwappedHalvesReuseAndDisposedCleanupWorkAcrossReleases()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var t1 = ScriptableObject.CreateInstance<TestTarget>();
            var t2 = ScriptableObject.CreateInstance<TestTarget>();
            var hA = new Hash128(0x1122334455667788UL, 0x99aabbccddeeff00UL);
            var hSwapped = new Hash128(0x99aabbccddeeff00UL, 0x1122334455667788UL);
            var hC = Hash128.Compute("unbound-hash128-unit");
            t1.hashVal = hA;
            t2.hashVal = hSwapped;

            using var panel = RuntimePanel.Create(settings);
            using var _ = TestRelease.Pin(TestRelease.Unmeasured);
            var soMulti = new SerializedObject(new Object[] { t1, t2 });
            var soSingle = new SerializedObject(t1);
            try
            {
                // Swapped halves must produce distinct contentHash values and report mixed state.
                var pSingle = soSingle.FindProperty("hashVal");
                Assert.Equal(SerializedPropertyType.Hash128, pSingle.propertyType);
                Assert.True(pSingle.hasVisibleChildren);
                uint hashA = pSingle.contentHash;
                pSingle.hash128Value = hSwapped;
                Assert.NotEqual(hashA, pSingle.contentHash);
                pSingle.hash128Value = hA;
                soSingle.ApplyModifiedProperties();

                var rt = new RuntimeModel { text = "rt-hash-0" };
                var hf = new Hash128Field { dataSource = rt };
                hf.SetBinding("tooltip", new DataBinding
                {
                    dataSourcePath = new PropertyPath(nameof(RuntimeModel.text)),
                    bindingMode = BindingMode.ToTarget
                });
                panel.visualTree.Add(hf);
                hf.BindProperty(soMulti.FindProperty("hashVal"));
                Tick(panel);

                Assert.True(hf.showMixedValue);
                Assert.Equal(BaseField<Hash128>.mixedValueString, hf.text);
                Assert.Equal("rt-hash-0", hf.tooltip);

                // Editing mixed Hash128Field to target[0]'s value writes both targets and clears mixed.
                hf.value = hA;
                Tick(panel);
                Assert.False(hf.showMixedValue);
                Assert.Equal(hA, t1.hashVal);
                Assert.Equal(hA, t2.hashVal);
                Assert.Equal(hA.ToString(), hf.text);

                // PropertyField creates and reuses Hash128Field and updates on external swapped-halves mutation.
                var pf = new PropertyField(soSingle.FindProperty("hashVal"));
                panel.visualTree.Add(pf);
                pf.Bind(soSingle);
                Tick(panel);
                var pfChild = Assert.IsType<Hash128Field>(pf.hierarchy[0]);
                Assert.Equal("Hash Val", pfChild.label);
                Assert.Equal(hA, pfChild.value);

                t1.hashVal = hSwapped;
                Tick(panel);
                Assert.Equal(hSwapped, pfChild.value);
                Assert.Equal(hSwapped.ToString(), pfChild.text);

                pf.BindProperty(soSingle.FindProperty("otherHashVal"));
                Tick(panel);
                Assert.Same(pfChild, pf.hierarchy[0]);
                Assert.Equal(t1.otherHashVal, pfChild.value);

                // Disposing SerializedObject unbinds cleanly while runtime DataBinding remains active.
                soMulti.Dispose();
                hf.value = hC;
                rt.text = "rt-hash-1";
                Tick(panel);
                Assert.Equal(hA, t2.hashVal);
                Assert.Equal("rt-hash-1", hf.tooltip);
            }
            finally
            {
                soMulti.Dispose();
                soSingle.Dispose();
                Object.DestroyImmediate(t1);
                Object.DestroyImmediate(t2);
                Object.DestroyImmediate(settings);
            }
        }

        sealed class EmptyUxmlAttributes : IUxmlAttributes
        {
            public bool TryGetAttributeValue(string attributeName, out string value)
            {
                value = null;
                return false;
            }
        }
    }
}

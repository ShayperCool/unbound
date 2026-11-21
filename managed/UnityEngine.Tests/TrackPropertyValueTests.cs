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
            Read = 1,
            Write = 2
        }

        [Serializable]
        struct NestedPayload
        {
            public int score;
            public string tag;
        }

        sealed class TestTarget : ScriptableObject
        {
            public bool flag = true;
            public int count = 10;
            public int other = 1;
            public string label = "alpha";
            public Vector3 pos = new Vector3(1, 2, 3);
            public NestedPayload nested = new NestedPayload { score = 5, tag = "n0" };
            public string[] items = new[] { "x", "y" };
            public SparseChoice sparseEnum = SparseChoice.TwentyFive;
            public SignedChoice signedEnum = SignedChoice.MinusFive;
            public NonMonotonicAliasChoice aliasEnum = NonMonotonicAliasChoice.Primary;
            public BitFlagsChoice flagsEnum = BitFlagsChoice.Read;
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

                // Unsupported property type (Vector3) reports explicit Stubs.NotImplemented hit and stays unbound.
                const string vecStubKey = "UnityEditor.UIElements.BindingExtensions.BindProperty (Vector3)";
                int beforeVecHits = Stubs.HitReport().FirstOrDefault(h => h.Key == vecStubKey).Value;
                var vecField = new IntegerField();
                panel.visualTree.Add(vecField);
                vecField.BindProperty(so.FindProperty("pos"));
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
                const string vecStubKey = "UnityEditor.UIElements.PropertyField (Vector3)";
                int beforeHits = Stubs.HitReport().FirstOrDefault(h => h.Key == vecStubKey).Value;
                var pf = new PropertyField(so.FindProperty("pos"));
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
        public void EnumBindingFlagsReportStubAndScalarControlsBindEnum()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var target = ScriptableObject.CreateInstance<TestTarget>();
            using var panel = RuntimePanel.Create(settings);
            using var so = new SerializedObject(target);
            try
            {
                const string pfFlagsKey = "UnityEditor.UIElements.PropertyField (EnumFlags)";
                const string efFlagsKey = "UnityEditor.UIElements.Bindings.SerializedManagedEnumBinding (Flags)";
                int beforePf = Stubs.HitReport().FirstOrDefault(h => h.Key == pfFlagsKey).Value;
                int beforeEf = Stubs.HitReport().FirstOrDefault(h => h.Key == efFlagsKey).Value;

                var pfFlags = new PropertyField(so.FindProperty("flagsEnum"));
                var efFlags = new EnumField();
                panel.visualTree.Add(pfFlags);
                panel.visualTree.Add(efFlags);
                pfFlags.Bind(so);
                efFlags.BindProperty(so.FindProperty("flagsEnum"));
                Assert.Equal(0, pfFlags.hierarchy.childCount);
                Assert.True(Stubs.HitReport().FirstOrDefault(h => h.Key == pfFlagsKey).Value > beforePf);
                Assert.True(Stubs.HitReport().FirstOrDefault(h => h.Key == efFlagsKey).Value > beforeEf);

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
    }
}

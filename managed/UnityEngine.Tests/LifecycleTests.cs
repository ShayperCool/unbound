using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public class DeactivatesOnEnable : MonoBehaviour
    {
        void OnEnable() => gameObject.SetActive(false);
    }

    public class RecordsEnableDisable : MonoBehaviour
    {
        public readonly List<string> log = new List<string>();
        void OnEnable() => log.Add("enable");
        void OnDisable() => log.Add("disable");
    }

    public class RecordsQuit : MonoBehaviour
    {
        public int calls;
        void OnApplicationQuit() => calls++;
    }

    public class RecordsFunctionPointerMessage : MonoBehaviour
    {
        public int enableCalls;
        public int lateCalls;

        void OnEnable() => enableCalls++;

        unsafe delegate*<void> LateUpdate()
        {
            lateCalls++;
            return null;
        }
    }

    public class RecordsRefFunctionPointerMessage : RecordsFunctionPointerMessage
    {
        unsafe delegate*<void> result;

        unsafe ref delegate*<void> LateUpdate()
        {
            lateCalls++;
            return ref result;
        }
    }

    public class RecordsIndirectFunctionPointerMessage : RecordsFunctionPointerMessage
    {
        unsafe delegate*<void>* LateUpdate()
        {
            lateCalls++;
            return null;
        }
    }

    public class RecordsRefMessage : RecordsFunctionPointerMessage
    {
        int result;

        ref int LateUpdate()
        {
            lateCalls++;
            return ref result;
        }
    }

    public class RecordsPointerMessage : RecordsFunctionPointerMessage
    {
        unsafe int* LateUpdate()
        {
            lateCalls++;
            return null;
        }
    }

    public class RecordsSpanMessage : RecordsFunctionPointerMessage
    {
        Span<int> LateUpdate()
        {
            lateCalls++;
            return default;
        }
    }

    public class RecordsConcreteStart : MonoBehaviour
    {
        internal sealed class Routine : IEnumerator
        {
            public object Current => null;
            public bool MoveNext() => false;
            public void Reset() { }
        }

        internal readonly Routine routine = new Routine();

        Routine Start() => routine;
    }

    public class LifecycleTests
    {
        [Fact]
        public void QuitReachesDisabledScriptsOnActiveObjectsOnly()
        {
            // Unity 6000.0.71f1: editorcallbacks leave-play controls, with and without domain reload.
            using var scenes = new Unbound.Tests.LightProbesTests.Scenes();
            scenes.Loaded("Quit");
            var active = new GameObject("active").AddComponent<RecordsQuit>();
            var disabled = new GameObject("disabled").AddComponent<RecordsQuit>();
            disabled.enabled = false;
            var inactive = new GameObject("inactive").AddComponent<RecordsQuit>();
            inactive.gameObject.SetActive(false);
            try
            {
                Lifecycle.Broadcast(m => m.OnApplicationQuit, null);
                Assert.Equal(1, active.calls);
                Assert.Equal(1, disabled.calls);
                Assert.Equal(0, inactive.calls);
            }
            finally
            {
                Object.DestroyImmediate(active.gameObject);
                Object.DestroyImmediate(disabled.gameObject);
                Object.DestroyImmediate(inactive.gameObject);
            }
        }

        [Theory]
        [InlineData(typeof(RecordsFunctionPointerMessage))]
        [InlineData(typeof(RecordsRefFunctionPointerMessage))]
        [InlineData(typeof(RecordsIndirectFunctionPointerMessage))]
        public void AFunctionPointerResultDoesNotBlockScriptMessages(Type type)
        {
            var go = new GameObject("callback test");
            try
            {
                var script = (RecordsFunctionPointerMessage)go.AddComponent(type);
                ScriptMessages.For(type).LateUpdate(script);
                Assert.Equal(1, script.enableCalls);
                Assert.Equal(1, script.lateCalls);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Fact]
        public void AConcreteEnumeratorSurvivesTheStartReturnAdapter()
        {
            var go = new GameObject("coroutine test");
            try
            {
                var script = go.AddComponent<RecordsConcreteStart>();
                Assert.Same(script.routine, ScriptMessages.For(script.GetType()).StartCoroutine(script));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Fact]
        public void BehavioursGetOnDisableOnlyAfterOnEnable()
        {
            var go = new GameObject("lifecycle");
            go.SetActive(false);
            go.AddComponent<DeactivatesOnEnable>();
            var recorder = go.AddComponent<RecordsEnableDisable>();
            // The first script deactivates the object before the second one's turn: the second gets
            // neither OnEnable nor OnDisable.
            go.SetActive(true);
            Assert.Empty(recorder.log);
            Object.DestroyImmediate(go);
            Assert.Empty(recorder.log);
        }

        [Fact]
        public void DisablingAnEnabledBehaviourSendsOnDisableOnce()
        {
            var go = new GameObject("lifecycle");
            var recorder = go.AddComponent<RecordsEnableDisable>();
            recorder.enabled = false;
            go.SetActive(false);
            recorder.enabled = true;
            go.SetActive(true);
            Object.DestroyImmediate(go);
            Assert.Equal(new[] { "enable", "disable", "enable", "disable" }, recorder.log);
        }
    }

    [Collection(CollectibleContexts.Name)]
    public sealed class CollectibleScriptMessageTests
    {
        [Theory]
        [InlineData(typeof(RecordsRefMessage))]
        [InlineData(typeof(RecordsPointerMessage))]
        [InlineData(typeof(RecordsSpanMessage))]
        public void ADiscardedReturnKeepsCollectibleScriptMessagesAvailable(Type original)
        {
            CollectibleContexts.Use("discarded callback return", context =>
            {
                using var image = File.OpenRead(original.Assembly.Location);
                var type = context.LoadFromStream(image).GetType(original.FullName, throwOnError: true);
                // Only test dispatch here, without registering an object owned by this context.
                var script = (MonoBehaviour)RuntimeHelpers.GetUninitializedObject(type);
                try
                {
                    ScriptMessages.For(type).LateUpdate(script);
                    Assert.Equal(1, type.GetField(nameof(RecordsFunctionPointerMessage.lateCalls)).GetValue(script));
                }
                finally
                {
                    ScriptMessages.ClearCache();
                }
            });
        }
    }
}

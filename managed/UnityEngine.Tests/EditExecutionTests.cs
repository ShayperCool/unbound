using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public abstract class EditCounterBase : MonoBehaviour
    {
        public readonly List<string> Calls = new List<string>();
        protected void Awake() => Calls.Add("Awake");
        protected void OnEnable() => Calls.Add("OnEnable");
        protected void Update() => Calls.Add("Update");
        protected void OnDisable() => Calls.Add("OnDisable");
    }
    [ExecuteInEditMode] public abstract class InheritedEditBase<T> : EditCounterBase { }
    public class InheritedEditCounter : InheritedEditBase<InheritedEditCounter> { }
    [ExecuteAlways] public abstract class InheritedAlwaysBase : EditCounterBase { }
    public class InheritedAlwaysCounter : InheritedAlwaysBase { }
    public class PlainEditCounter : EditCounterBase { }

    [Collection("Frames")]
    public class EditExecutionTests
    {
        [Theory]
        [InlineData(typeof(InheritedEditCounter))]
        [InlineData(typeof(InheritedAlwaysCounter))]
        public void InheritedAttributesDispatchEditMessagesAndQueuedUpdates(Type type)
        {
            bool playing = EditorSession.IsPlaying;
            EditorSession.IsPlaying = false;
            GameObject go = null;
            try
            {
                go = new GameObject("edit execution");
                var counter = (EditCounterBase)go.AddComponent(type);
                Assert.Equal(new[] { "Awake", "OnEnable" }, counter.Calls);
                EditorSession.RunQueuedEditModeUpdate();
                counter.Calls.Clear();
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
                EditorSession.RunQueuedEditModeUpdate();
                Assert.Equal(new[] { "Update" }, counter.Calls);
                EditorSession.RunQueuedEditModeUpdate();
                Assert.Single(counter.Calls);
                counter.enabled = false;
                Assert.Equal("OnDisable", counter.Calls[1]);
            }
            finally
            {
                if (go != null) Object.DestroyImmediate(go);
                EditorSession.IsPlaying = playing;
            }
        }

        [Fact]
        public void PlainScriptsStayAsleepUntilActivePlayActivation()
        {
            bool playing = EditorSession.IsPlaying;
            EditorSession.IsPlaying = false;
            var go = new GameObject("plain execution");
            try
            {
                var counter = go.AddComponent<PlainEditCounter>();
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
                EditorSession.RunQueuedEditModeUpdate();
                Assert.Empty(counter.Calls);
                EditorSession.IsPlaying = true;
                Lifecycle.Activate(new List<Component> { counter });
                Lifecycle.RunUpdate();
                Assert.Equal(new[] { "Awake", "OnEnable", "Update" }, counter.Calls);
                Lifecycle.Activate(new List<Component> { counter });
                Assert.Equal(3, counter.Calls.Count);
            }
            finally
            {
                Object.DestroyImmediate(go);
                EditorSession.IsPlaying = playing;
            }
        }

        [Fact]
        public void RegistrationRefreshKeepsDormantStartupScriptsAsleep()
        {
            bool playing = EditorSession.IsPlaying;
            EditorSession.IsPlaying = false;
            var go = new GameObject("dormant startup");
            try
            {
                var counter = go.AddComponent<PlainEditCounter>();
                EditorSession.IsPlaying = true;
                Object.DontDestroyOnLoad(go);
                Lifecycle.RefreshRegistrations();
                Lifecycle.RunPendingStarts();
                Lifecycle.RunUpdate();
                Assert.Empty(counter.Calls);
                Assert.False(counter.didAwake);
                Assert.False(counter.didStart);

                // A real enable transition still awakens and registers the script.
                counter.enabled = false;
                counter.enabled = true;
                Lifecycle.RefreshRegistrations();
                Lifecycle.RunUpdate();
                Assert.Equal(new[] { "Awake", "OnEnable", "Update" }, counter.Calls);
                Assert.True(counter.didAwake);
                Assert.True(counter.didStart);
            }
            finally
            {
                Object.DestroyImmediate(go);
                EditorSession.IsPlaying = playing;
            }
        }
    }
}

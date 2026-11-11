using System;
using System.Collections.Generic;
using System.Reflection;
using Unbound.Runtime;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    [Collection("Frames")]
    public class RenderCallbackTests
    {
        const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
        static readonly Func<ScriptMessages, Action<MonoBehaviour>> PreCull = m => m.OnPreCull;
        static readonly Func<ScriptMessages, Action<MonoBehaviour>> RenderObject = m => m.OnRenderObject;
        static readonly Action<Camera, Func<ScriptMessages, Action<MonoBehaviour>>> SendCamera =
            typeof(FrameBuilder).GetMethod("SendToCamera", PrivateStatic)
                .CreateDelegate<Action<Camera, Func<ScriptMessages, Action<MonoBehaviour>>>>();
        static readonly Action<Func<ScriptMessages, Action<MonoBehaviour>>> SendAll =
            typeof(FrameBuilder).GetMethod("SendToAll", PrivateStatic)
                .CreateDelegate<Action<Func<ScriptMessages, Action<MonoBehaviour>>>>();

        public class Added : MonoBehaviour
        {
            public int Calls;
            void OnPreCull() { Calls++; }
        }
        public class Adding : MonoBehaviour
        {
            public Added Added;
            void OnPreCull() { if (Added == null) Added = gameObject.AddComponent<Added>(); }
        }
        public class Counter : MonoBehaviour
        {
            public int Calls;
            void OnRenderObject() { Calls++; }
        }

        [Fact]
        public void CameraMessageVisitsComponentsAddedDuringThatMessage()
        {
            var go = new GameObject("camera");
            try
            {
                var camera = go.AddComponent<Camera>();
                var adding = go.AddComponent<Adding>();
                SendCamera(camera, PreCull);
                Assert.Equal(1, adding.Added.Calls);
                SendCamera(camera, PreCull);
                Assert.Equal(2, adding.Added.Calls);
                adding.Added.enabled = false;
                SendCamera(camera, PreCull);
                Assert.Equal(2, adding.Added.Calls);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Theory]
        [InlineData(1, true)]
        [InlineData(2, true)]
        [InlineData(1, false)]
        [InlineData(2, false)]
        public void FrameAwaitablesCompleteAfterRenderingAndWaitAcrossSkippedFrames(int interval, bool enteredPlayMode)
        {
            var driver = typeof(PlayerLoopDriver);
            var rendering = driver.GetField("s_FrameRendering", PrivateStatic);
            var end = driver.GetField("s_EndOfFrameAfterLoop", PrivateStatic);
            var advance = driver.GetField("s_AdvanceAfterLoop", PrivateStatic);
            var thread = driver.GetField("s_MainThreadId", PrivateStatic);
            var entered = driver.GetField("s_EnteredPlayMode", PrivateStatic);
            var savedEntered = entered.GetValue(null);
            var savedRendering = rendering.GetValue(null); var savedEnd = end.GetValue(null);
            var savedAdvance = advance.GetValue(null); var savedThread = thread.GetValue(null);
            var release = ProjectInfo.UnityVersion; int frame = Time.s_FrameCount;
            int previousInterval = Rendering.OnDemandRendering.s_FrameInterval;
            bool rendered = false, resumed = false, resumedAfterRender = false;
            Awaitable waiting = null;
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                thread.SetValue(null, Environment.CurrentManagedThreadId);
                entered.SetValue(null, enteredPlayMode);
                Rendering.OnDemandRendering.s_FrameInterval = interval;
                Time.s_FrameCount = 5;
                rendering.SetValue(null, (Action)(() => rendered = true));
                advance.SetValue(null, false);
                waiting = Awaitable.EndOfFrameAsync();
                waiting.GetAwaiter().OnCompleted(() => { resumedAfterRender = rendered; resumed = true; });
                var engine = (Dictionary<Type, List<Action>>)driver.GetField("s_Engine", PrivateStatic).GetValue(null);
                void Trigger()
                {
                    foreach (var action in engine[typeof(PlayerLoop.PostLateUpdate.TriggerEndOfFrameCallbacks)]) action();
                }
                Trigger(); Assert.False(resumed);
                driver.GetMethod("AfterLoop", PrivateStatic).Invoke(null, null);
                if (!enteredPlayMode || interval == 2)
                {
                    Assert.False(resumed);
                    if (!enteredPlayMode) Assert.True(rendered);
                    entered.SetValue(null, true);
                    Time.s_FrameCount = 6; rendered = false;
                    Trigger(); Assert.False(resumed);
                    driver.GetMethod("AfterLoop", PrivateStatic).Invoke(null, null);
                }
                Assert.True(resumed); Assert.True(resumedAfterRender);
            }
            finally
            {
                if (!resumed) waiting?.Cancel();
                rendering.SetValue(null, savedRendering); end.SetValue(null, savedEnd);
                advance.SetValue(null, savedAdvance); thread.SetValue(null, savedThread);
                entered.SetValue(null, savedEntered);
                ProjectInfo.UnityVersion = release; Time.s_FrameCount = frame;
                Rendering.OnDemandRendering.s_FrameInterval = previousInterval;
            }
        }

        [Fact]
        public void RenderObjectDispatchReusesItsSnapshotAfterWarmup()
        {
            var scripts = (List<MonoBehaviour>)typeof(Lifecycle).GetField("s_OnRenderObject", PrivateStatic).GetValue(null);
            var previous = scripts.ToArray();
            scripts.Clear();
            var go = new GameObject("renderer-script");
            try
            {
                var counter = go.AddComponent<Counter>();
                for (int i = 0; i < 64; i++) SendAll(RenderObject);
                counter.Calls = 0;
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 256; i++) SendAll(RenderObject);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.Equal(256, counter.Calls);
                Assert.Equal(0, allocated);
            }
            finally
            {
                Object.DestroyImmediate(go);
                scripts.Clear(); scripts.AddRange(previous);
            }
        }
    }
}

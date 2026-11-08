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

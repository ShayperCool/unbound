using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>OnWillRenderObject from a camera's culling: once per enabled script on the objects
    /// of the renderers the camera sees (in the order they were turned on), none for renderers out
    /// of view, on other layers or with disabled scripts; a script may change the scene meanwhile:
    /// a renderer it turns off gets no message, one it turns on waits for the next culling.</summary>
    public sealed class WillRenderObjectTests : IDisposable
    {
        const int Layer = 29;
        static readonly List<string> s_Calls = new List<string>();
        readonly List<GameObject> m_Objects = new List<GameObject>();

        public WillRenderObjectTests() => s_Calls.Clear();

        public void Dispose()
        {
            foreach (var o in m_Objects) if (o != null) Object.DestroyImmediate(o);
            s_Calls.Clear();
        }

        public sealed class Tell : MonoBehaviour
        {
            public Action onWillRender;
            void OnWillRenderObject()
            {
                s_Calls.Add(name);
                onWillRender?.Invoke();
            }
        }

        public sealed class Silent : MonoBehaviour { }

        GameObject Cube(string name, Vector3 position, bool script = true, int layer = Layer)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m_Objects.Add(cube);
            cube.name = name;
            cube.layer = layer;
            cube.transform.position = position;
            cube.AddComponent<Silent>();
            if (script) cube.AddComponent<Tell>();
            return cube;
        }

        void Cull()
        {
            var cameraObject = new GameObject("camera");
            m_Objects.Add(cameraObject);
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 0, -10);
            camera.cullingMask = 1 << Layer;
            try
            {
                Assert.True(camera.TryGetCullingParameters(out var parameters));
                Culling.Cull(ref parameters);
            }
            finally { Culling.EndFrame(); Object.DestroyImmediate(cameraObject); }
        }

        [Fact]
        public void TheSeenObjectsWithEnabledScriptsAreTold()
        {
            Cube("plain", Vector3.zero, script: false);
            Cube("first", new Vector3(1, 0, 0));
            Cube("away", new Vector3(500, 0, 0));
            Cube("other layer", new Vector3(-1, 0, 0), layer: 28);
            Cube("disabled", new Vector3(0, 1, 0)).GetComponent<Tell>().enabled = false;
            Cube("second", new Vector3(0, -1, 0));
            Cull();
            Assert.Equal(new[] { "first", "second" }, s_Calls);
        }

        [Fact]
        public void AScriptChangesTheSceneWhileTheCameraTellsTheOthers()
        {
            var first = Cube("first", Vector3.zero);
            var hidden = Cube("hidden", new Vector3(1, 0, 0));
            var late = Cube("late", new Vector3(-1, 0, 0));
            late.GetComponent<MeshRenderer>().enabled = false;
            var enabled = Cube("enabled", new Vector3(0, 1, 0));
            enabled.GetComponent<Tell>().enabled = false;
            first.GetComponent<Tell>().onWillRender = () =>
            {
                hidden.SetActive(false);
                late.GetComponent<MeshRenderer>().enabled = true;
                enabled.GetComponent<Tell>().enabled = true;
            };
            Cull();
            Assert.Equal(new[] { "first", "enabled" }, s_Calls);
            s_Calls.Clear();
            first.GetComponent<Tell>().onWillRender = null;
            Cull();
            Assert.Equal(new[] { "first", "enabled", "late" }, s_Calls);
        }

        /// <summary>A script that culls a camera from OnWillRenderObject (a reflection camera)
        /// gets the nested pass's messages; the outer pass still tells the objects after it.</summary>
        [Fact]
        public void ANestedCullingLeavesTheOuterPassWhole()
        {
            var first = Cube("first", Vector3.zero);
            Cube("second", new Vector3(1, 0, 0));
            bool nested = false;
            first.GetComponent<Tell>().onWillRender = () =>
            {
                if (nested) return;
                nested = true;
                Cull();
            };
            Cull();
            Assert.Equal(new[] { "first", "first", "second", "second" }, s_Calls);
        }
    }
}

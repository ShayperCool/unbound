using System;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A class deriving from Object outside the engine, with no engine class between.</summary>
    public class PlainObjectSubclass : Object
    {
    }

    /// <summary>A plain Object made with <c>new</c> has no engine object behind it (Unity 6000.0.71f1,
    /// tests/projects/defaultstate UnityEngine.Object rows).</summary>
    public class PlainObjectWrapperTests : IDisposable
    {
        readonly IDisposable m_Release = TestRelease.Pin(TestRelease.Measured);

        public void Dispose() => m_Release.Dispose();

        [Fact]
        public void APlainObjectsNameAndHideFlagsThrow()
        {
            foreach (var o in new Object[] { new Object(), new PlainObjectSubclass() })
            {
                Assert.Throws<NullReferenceException>(() => o.name);
                Assert.Throws<NullReferenceException>(() => o.hideFlags);
                Assert.Throws<NullReferenceException>(() => o.name = "x");
                Assert.True(o == null);
                Assert.Equal(0, o.GetInstanceID());
            }
        }

        [Fact]
        public void TheEnginesOwnPlainObjectsAreLive()
        {
            // PrefabUtility.GetPrefabInstanceHandle's object (tests/projects/editorcallbacks: a live
            // handle) and ObjectFactory's plain Object are made by the engine.
            foreach (bool inAsset in new[] { false, true })
            {
                var handle = new Unbound.Runtime.PrefabInstanceData { InAsset = inAsset }.Handle;
                Assert.False(handle == null);
                Assert.NotEqual(0, handle.GetInstanceID());
                Assert.Equal(inAsset, handle.GetInstanceID() > 0);
                Assert.Equal("", handle.name);
                Object.DestroyImmediate(handle, true);
            }
            var made = UnityEditor.ObjectFactory.CreateInstance(typeof(Object));
            Assert.False(made == null);
            Assert.Equal("", made.name);
            Object.DestroyImmediate(made);
        }

        [Fact]
        public void EngineObjectsAreStillCreated()
        {
            var material = new Material(Shader.Find("Hidden/InternalErrorShader")) { name = "made" };
            var asset = ScriptableObject.CreateInstance<ScriptableObject>();
            try
            {
                Assert.Equal("made", material.name);
                Assert.Equal(HideFlags.None, asset.hideFlags);
                Assert.False(material == null);
                Assert.NotEqual(0, asset.GetInstanceID());
            }
            finally
            {
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(asset);
            }
        }
    }
}

using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Scripts may override GetHashCode and Equals with members that need a live object
    /// (the render pipeline core's ProbeVolume hashes gameObject.transform). The engine keys its
    /// own bookkeeping by object identity: enabling, disabling, instantiating, destroying and reading
    /// the component index of such scripts (with update, gizmo, OnGUI and OnRenderObject messages)
    /// never calls their GetHashCode or Equals, so destroying them does not throw a
    /// MissingReferenceException from inside the engine.</summary>
    [Collection("Frames")]
    public class DestroyHashCodeTests
    {
        public class HashesItsTransform : MonoBehaviour
        {
            internal static int Calls;

            void Update() { }
            void LateUpdate() { }
            void FixedUpdate() { }
            void OnDrawGizmos() { }
            void OnGUI() { }
            void OnRenderObject() { }

            public override int GetHashCode()
            {
                Calls++;
                return gameObject.transform.worldToLocalMatrix.GetHashCode();
            }

            public override bool Equals(object other)
            {
                Calls++;
                return other is HashesItsTransform h && h.gameObject.transform == gameObject.transform;
            }
        }

        [Fact]
        public void TheEngineNeverHashesScriptsThroughTheirOwnOverrides()
        {
            HashesItsTransform.Calls = 0;
            var parent = new GameObject("parent");
            var child = new GameObject("child");
            child.transform.SetParent(parent.transform);
            var a = parent.AddComponent<HashesItsTransform>();
            var b = child.AddComponent<HashesItsTransform>();
            b.enabled = false;
            b.enabled = true;
            var copy = Object.Instantiate(parent);
            Assert.Equal(1, parent.GetComponentIndex(a));
            var lone = new GameObject("lone").AddComponent<HashesItsTransform>();

            Object.DestroyImmediate(lone);
            Object.DestroyImmediate(copy);
            Object.DestroyImmediate(parent);

            Assert.True(a == null);
            Assert.True(b == null);
            Assert.True(child == null);
            Assert.True(copy == null);
            Assert.Equal(0, HashesItsTransform.Calls);
        }
    }

    public class RawMemoryHashTests
    {
        /// <summary>Unbound hashes at most int.MaxValue bytes of raw memory in one call and says so;
        /// nothing is read before the check.</summary>
        [Fact]
        public unsafe void HashingMoreThanTheLimitFailsClearly()
        {
            var memory = (byte*)System.Runtime.InteropServices.NativeMemory.Alloc(16);
            var hash = (Hash128*)(memory + 0);
            *hash = new Hash128();
            System.ArgumentException e;
            try
            {
                e = Assert.Throws<System.ArgumentException>(() => HashUnsafeUtilities.ComputeHash128(memory, (ulong)int.MaxValue + 1, hash));
            }
            finally
            {
                System.Runtime.InteropServices.NativeMemory.Free(memory);
            }
            Assert.Contains("at most", e.Message);
        }
    }
}

using System;
using UnityEditor.Build.Content;
using Xunit;

namespace UnityEngine.Tests
{
    // ObjectIdentifier is a serializable struct without serialized fields, as measured on the Unity
    // 6000.0.71f1 Editor in tests/projects/objectidentifier; these tests take it inside an array and
    // in a JSON round trip of a ScriptableObject, which the capture does not.
    public sealed class ObjectIdentifierSerializationTests
    {
        [Serializable] public class Holder { public ObjectIdentifier[] identifiers = new ObjectIdentifier[0]; public int control; }
        public class Asset : ScriptableObject { public ObjectIdentifier identifier = Make(29); public int control = 19; }

        static ObjectIdentifier Make(long fileId) => new ObjectIdentifier { localIdentifierInFile = fileId, filePath = "a.asset" };

        // Arrays are Unbound's choice, unmeasured: each element is an empty object and reads back as the default.
        [Fact]
        public void ItSerializesAsAnEmptyObject()
        {
            Assert.True(typeof(ObjectIdentifier).IsSerializable);
            var holder = new Holder { identifiers = new[] { Make(17), Make(18) }, control = 3 };
            Assert.Equal("{\"identifiers\":[{},{}],\"control\":3}", JsonUtility.ToJson(holder));
            var restored = JsonUtility.FromJson<Holder>(JsonUtility.ToJson(holder));
            Assert.Equal(2, restored.identifiers.Length);
            Assert.Equal(0, restored.identifiers[0].localIdentifierInFile);
        }

        [Fact]
        public void AnAssetsJsonKeepsTheInitializer()
        {
            var source = ScriptableObject.CreateInstance<Asset>();
            var target = ScriptableObject.CreateInstance<Asset>();
            try
            {
                source.identifier = Make(17);
                source.control = 23;
                var json = JsonUtility.ToJson(source);
                Assert.Contains("\"identifier\":{}", json);
                JsonUtility.FromJsonOverwrite(json, target);
                Assert.Equal(29, target.identifier.localIdentifierInFile);
                Assert.Equal(23, target.control);
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(target);
            }
        }
    }
}

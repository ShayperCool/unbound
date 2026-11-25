using System;
using Unbound.Runtime;
using UnityEngine.Serialization;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// A field renamed with [FormerlySerializedAs] reads its old name's value when its own name is
    /// missing, and an old name beside the current one lies over it: a prefab variant's override
    /// written before the rename (propertyPath under the old name) reaches the renamed field over
    /// the base prefab's value, as in the Unity Editor, where such overrides apply.
    /// </summary>
    public class FormerNameOverrideTests
    {
        [Serializable] public class Pair { public int a; public int b; }

        public class Asset : ScriptableObject
        {
            [FormerlySerializedAs("oldNumber")] public int number;
            [FormerlySerializedAs("oldPair")] public Pair pair;
            [FormerlySerializedAs("oldTarget")] public Asset target;
        }

        static Asset Read(string yaml, IReferenceResolver resolver = null)
        {
            var asset = ScriptableObject.CreateInstance<Asset>();
            new YamlReader(resolver).ReadScriptObject(asset, TestYaml.DocumentBody(yaml));
            return asset;
        }

        [Fact]
        public void TheCurrentNameAloneIsRead()
        {
            Assert.Equal(1, Read("MonoBehaviour:\n  number: 1\n").number);
        }

        [Fact]
        public void TheOldNameAloneIsRead()
        {
            Assert.Equal(3, Read("MonoBehaviour:\n  oldNumber: 3\n").number);
        }

        [Fact]
        public void AnOldNameBesideTheCurrentOneLiesOverIt()
        {
            var asset = Read("MonoBehaviour:\n  number: 1\n  oldNumber: 2\n  pair:\n    a: 1\n    b: 2\n  oldPair:\n    b: 5\n");
            Assert.Equal(2, asset.number);
            // A composite takes the old name's members over the current name's, member by member.
            Assert.Equal(1, asset.pair.a);
            Assert.Equal(5, asset.pair.b);
        }

        [Fact]
        public void AnOldNameReferenceBesideANullCurrentOneSetsTheField()
        {
            var other = ScriptableObject.CreateInstance<Asset>();
            var asset = Read("MonoBehaviour:\n  target: {fileID: 0}\n  oldTarget: {fileID: 7}\n", new OneObject { Id = 7, Object = other });
            Assert.Same(other, asset.target);
        }

        sealed class OneObject : IReferenceResolver
        {
            public long Id;
            public Object Object;
            public Object Resolve(ObjectRef reference, Type expected) => reference.FileId == Id ? Object : null;
        }
    }
}

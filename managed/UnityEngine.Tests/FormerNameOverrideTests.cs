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
            [FormerlySerializedAs("oldList")] public int[] list;
        }

        /// <summary>A type that gives a renamed field's old name to a new field, as UGUI's
        /// ColorBlock (m_SelectedColor was m_HighlightedColor) and SpriteState do.</summary>
        public class Reused : ScriptableObject
        {
            public int m_Highlighted;
            [FormerlySerializedAs("m_Highlighted")] public int m_Selected;
            [FormerlySerializedAs("m_OldRef")] public Asset m_Ref;
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

        /// <summary>An override of one element of a renamed array (the flattened old name holds
        /// empty elements before it) keeps the base's other elements.</summary>
        [Fact]
        public void AnOldNameElementOverrideKeepsTheOtherElements()
        {
            var body = TestYaml.DocumentBody("MonoBehaviour:\n  list:\n  - 1\n  - 2\n  - 3\n  oldList:\n  - 0\n  - 9\n");
            body.Get("oldList").Items[0] = YNode.Null;
            var asset = ScriptableObject.CreateInstance<Asset>();
            new YamlReader(null).ReadScriptObject(asset, body);
            Assert.Equal(new[] { 1, 9, 3 }, asset.list);
        }

        [Fact]
        public void AnOldNameReferenceBesideANullCurrentOneSetsTheField()
        {
            var other = ScriptableObject.CreateInstance<Asset>();
            var asset = Read("MonoBehaviour:\n  target: {fileID: 0}\n  oldTarget: {fileID: 7}\n", new OneObject { Id = 7, Object = other });
            Assert.Same(other, asset.target);
        }

        [Fact]
        public void AnOldNameThatIsAnotherFieldsNameIsNoOverride()
        {
            var reused = ScriptableObject.CreateInstance<Reused>();
            new YamlReader(null).ReadScriptObject(reused, TestYaml.DocumentBody("MonoBehaviour:\n  m_Highlighted: 1\n  m_Selected: 2\n"));
            Assert.Equal(1, reused.m_Highlighted);
            Assert.Equal(2, reused.m_Selected);
            // Without its own name the field still reads the old one (a file saved before the rename).
            new YamlReader(null).ReadScriptObject(reused, TestYaml.DocumentBody("MonoBehaviour:\n  m_Highlighted: 3\n"));
            Assert.Equal(3, reused.m_Selected);
        }

        /// <summary>A local reference override over a base reference into another asset replaces
        /// it whole (no guid of the base's left on the override's file id).</summary>
        [Fact]
        public void AReferenceOverrideReplacesTheWholeReference()
        {
            var local = ScriptableObject.CreateInstance<Asset>();
            var foreign = ScriptableObject.CreateInstance<Asset>();
            var reused = ScriptableObject.CreateInstance<Reused>();
            new YamlReader(new Refs { Local = local, Foreign = foreign }).ReadScriptObject(reused,
                TestYaml.DocumentBody("MonoBehaviour:\n  m_Ref: {fileID: 11400000, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 2}\n  m_OldRef: {fileID: 7}\n"));
            Assert.Same(local, reused.m_Ref);
        }

        sealed class Refs : IReferenceResolver
        {
            public Object Local, Foreign;
            public Object Resolve(ObjectRef reference, Type expected) =>
                reference.FileId == 7 && reference.Guid == null ? Local : reference.Guid != null ? Foreign : null;
        }

        sealed class OneObject : IReferenceResolver
        {
            public long Id;
            public Object Object;
            public Object Resolve(ObjectRef reference, Type expected) => reference.FileId == Id ? Object : null;
        }
    }
}

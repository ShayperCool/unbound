using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>
    /// A Material as its SerializedObject sees it: the Material class's type tree (field order,
    /// type names, property types, the hidden fields) over the material's data written as YAML, and
    /// applied changes read back as a loaded material's data is (<see cref="Material.ApplySerialized"/>).
    /// The tree, the saved property sheet's lists (sorted by name, see <see cref="Material.SavedSheet"/>),
    /// the keyword lists (the shader's declared keywords that are on, then the others, each sorted)
    /// and the upper-case disabled passes match the Unity 6000.0.71f1 Editor
    /// (tests/projects/toolapis matx.*). References are the objects' instance IDs, as the Editor's
    /// in-memory references read. Not measured: the order of tags, disabled passes and locked
    /// properties (sorted here), a variant's sheet (its overrides as loaded), the elements of
    /// m_BuildTextureStacks.
    /// </summary>
    internal sealed class MaterialTypeTree : ITypeTreeTarget
    {
        readonly Material m_Material;
        YText m_Body;

        internal MaterialTypeTree(Material material) { m_Material = material; }

        public TypeTreeNode[] Tree => s_Tree;

        public YText Body => m_Body ??= Write(m_Material, Ref);

        public string RootTypeName => "Material";

        public Object Resolve(ObjectRef reference) => InMemory.Resolve(reference, typeof(Object));

        public ObjectRef Reference(Object o) => InMemory.Of(o);

        public void Applied()
        {
            m_Material.ApplySerialized(Body.ToNode(), InMemory);
            EditorUtility.SetDirty(m_Material);
        }

        static readonly InstanceReferences InMemory = new InstanceReferences();

        /// <summary>References by instance ID (a file id without a GUID), else by asset location.</summary>
        sealed class InstanceReferences : IReferenceResolver
        {
            public Object Resolve(ObjectRef reference, Type expected)
            {
                if (reference.IsNull) return null;
                if (reference.Guid == null) return ObjectRegistry.FindById(unchecked((int)reference.FileId));
                try { return AssetDatabaseRuntime.Load(reference.Guid, reference.FileId, expected ?? typeof(Object)); }
                catch (Exception) { return null; }
            }

            internal ObjectRef Of(Object o) => o == null ? default : new ObjectRef(o.GetInstanceID(), null, 0);
        }

        // ---------------------------------------------------------------- the material's data

        static YText Scalar(string text) => YText.Scalar(text);

        static YText Number(object value) => YText.Scalar(TypeTreeSerialized.Number(value));

        static YText Text(string value) => YText.Scalar(UnityYamlWriter.Scalar(value ?? ""));

        static YText Ref(Object o)
        {
            var map = YText.Map(flow: true);
            map.Raw = null;
            map.Add("fileID", Number(o == null ? 0 : o.GetInstanceID()));
            return map;
        }

        static YText Vector2Map(Vector2 v)
        {
            var map = YText.Map(flow: true);
            map.Raw = null;
            map.Add("x", Number(v.x));
            map.Add("y", Number(v.y));
            return map;
        }

        static YText ColorMap(Vector4 c)
        {
            var map = YText.Map(flow: true);
            map.Raw = null;
            map.Add("r", Number(c.x));
            map.Add("g", Number(c.y));
            map.Add("b", Number(c.z));
            map.Add("a", Number(c.w));
            return map;
        }

        static YText Strings(IEnumerable<string> items)
        {
            var seq = YText.Seq();
            foreach (var item in items) seq.Items.Add(Text(item));
            if (seq.Items.Count == 0) seq.Flow = true;
            return seq;
        }

        static List<string> Sorted(IEnumerable<string> items)
        {
            var list = new List<string>(items);
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        /// <summary>The fields a material's file stores that Unbound models, laid out as its .mat
        /// file has them (the saved property sheet's lists as sequences of one-entry maps), with
        /// references as <paramref name="reference"/> writes them.</summary>
        internal static YText FileBody(Material m, Func<Object, YText> reference)
        {
            var body = Write(m, reference);
            var saved = body.Get("m_SavedProperties");
            for (int i = 0; i < saved.Keys.Count; i++)
            {
                var sheet = saved.Values[i];
                var list = YText.Seq(flow: sheet.Keys.Count == 0);
                for (int k = 0; k < sheet.Keys.Count; k++)
                {
                    var entry = YText.Map();
                    entry.Add(sheet.Keys[k], sheet.Values[k]);
                    list.Items.Add(entry);
                }
                saved.Values[i] = list;
            }
            return body;
        }

        static YText Write(Material m, Func<Object, YText> Ref)
        {
            var body = YText.Map();
            body.Add("m_ObjectHideFlags", Number((uint)m.hideFlags));
            body.Add("m_CorrespondingSourceObject", Ref(null));
            body.Add("m_PrefabInstance", Ref(null));
            body.Add("m_PrefabAsset", Ref(null));
            body.Add("m_Name", Text(m.name));
            body.Add("m_Shader", Ref(m.shader));
            var (valid, invalid) = m.SerializedKeywords();
            body.Add("m_ValidKeywords", Strings(valid));
            body.Add("m_InvalidKeywords", Strings(invalid));
            body.Add("m_LightmapFlags", Number((int)m.globalIlluminationFlags));
            body.Add("m_EnableInstancingVariants", Number(m.enableInstancing ? 1 : 0));
            body.Add("m_DoubleSidedGI", Number(m.doubleSidedGI ? 1 : 0));
            body.Add("m_CustomRenderQueue", Number(m.SerializedRenderQueue));
            var tags = YText.Map();
            foreach (var key in Sorted(m.m_Tags.Keys)) tags.Add(UnityYamlWriter.Scalar(key), Text(m.m_Tags[key]));
            if (tags.Keys.Count == 0) tags.Flow = true;
            body.Add("stringTagMap", tags);
            body.Add("disabledShaderPasses", Strings(Sorted(m.DisabledPassNames())));
            body.Add("m_LockedProperties", Text(string.Join(" ", Sorted(m.LockedPropertyNames()))));
            var saved = YText.Map();
            var textures = YText.Map();
            foreach (var id in m.SheetNames(Material.SheetKind.Textures))
            {
                var (texture, scale, offset) = m.SheetTexture(id);
                var env = YText.Map();
                env.Add("m_Texture", Ref(texture));
                env.Add("m_Scale", Vector2Map(scale));
                env.Add("m_Offset", Vector2Map(offset));
                textures.Add(Shader.IdToName(id), env);
            }
            saved.Add("m_TexEnvs", Flowed(textures));
            var ints = YText.Map();
            foreach (var id in m.SheetNames(Material.SheetKind.Ints)) ints.Add(Shader.IdToName(id), Number(m.SheetInt(id)));
            saved.Add("m_Ints", Flowed(ints));
            var floats = YText.Map();
            foreach (var id in m.SheetNames(Material.SheetKind.Floats)) floats.Add(Shader.IdToName(id), Number(m.SheetFloat(id)));
            saved.Add("m_Floats", Flowed(floats));
            var colors = YText.Map();
            foreach (var id in m.SheetNames(Material.SheetKind.Colors)) colors.Add(Shader.IdToName(id), ColorMap(m.SheetColor(id)));
            saved.Add("m_Colors", Flowed(colors));
            body.Add("m_SavedProperties", saved);
            body.Add("m_BuildTextureStacks", YText.Seq(flow: true));
            body.Add("m_AllowLocking", Number(1));
            return body;
        }

        static YText Flowed(YText map)
        {
            if (map.Keys.Count == 0) map.Flow = true;
            return map;
        }

        // ---------------------------------------------------------------- the type tree

        static TypeTreeNode N(string name, string type, SerializedPropertyType p, params TypeTreeNode[] children) =>
            new TypeTreeNode { Name = name, TypeName = type, Type = p, Children = children };

        static TypeTreeNode Hidden(TypeTreeNode node)
        {
            node.Hidden = true;
            return node;
        }

        static TypeTreeNode Pointer(string name, string type) =>
            N(name, "PPtr<" + type + ">", SerializedPropertyType.ObjectReference,
                N("m_FileID", "int", SerializedPropertyType.Integer), N("m_PathID", "long", SerializedPropertyType.Integer));

        static TypeTreeNode Str(string name) => N(name, "string", SerializedPropertyType.String);

        static TypeTreeNode Vector(string name, string type, TypeTreeNode element) =>
            N(name, type, SerializedPropertyType.Generic,
                Hidden(N("Array", "Array", SerializedPropertyType.Generic, N("size", "ArraySize", SerializedPropertyType.ArraySize), element)));

        static TypeTreeNode Pair(TypeTreeNode second) => N("data", "pair", SerializedPropertyType.Generic, Str("first"), second);

        static TypeTreeNode Float(string name) => N(name, "float", SerializedPropertyType.Float);

        static TypeTreeNode Vector2(string name) => N(name, "Vector2", SerializedPropertyType.Vector2, Float("x"), Float("y"));

        static readonly TypeTreeNode[] s_Tree =
        {
            Hidden(N("m_ObjectHideFlags", "uint", SerializedPropertyType.Integer)),
            Hidden(Pointer("m_CorrespondingSourceObject", "EditorExtension")),
            Hidden(Pointer("m_PrefabInstance", "PrefabInstance")),
            Hidden(Pointer("m_PrefabAsset", "Prefab")),
            Hidden(Str("m_Name")),
            Pointer("m_Shader", "Shader"),
            Vector("m_ValidKeywords", "vector", Str("data")),
            Vector("m_InvalidKeywords", "vector", Str("data")),
            N("m_LightmapFlags", "Enum", SerializedPropertyType.Enum),
            N("m_EnableInstancingVariants", "bool", SerializedPropertyType.Boolean),
            N("m_DoubleSidedGI", "bool", SerializedPropertyType.Boolean),
            N("m_CustomRenderQueue", "int", SerializedPropertyType.Integer),
            Vector("stringTagMap", "map", Pair(Str("second"))),
            Vector("disabledShaderPasses", "vector", Str("data")),
            Str("m_LockedProperties"),
            N("m_SavedProperties", "UnityPropertySheet", SerializedPropertyType.Generic,
                Vector("m_TexEnvs", "map", Pair(N("second", "UnityTexEnv", SerializedPropertyType.Generic,
                    Pointer("m_Texture", "Texture"), Vector2("m_Scale"), Vector2("m_Offset")))),
                Vector("m_Ints", "map", Pair(N("second", "int", SerializedPropertyType.Integer))),
                Vector("m_Floats", "map", Pair(Float("second"))),
                Vector("m_Colors", "map", Pair(N("second", "Color", SerializedPropertyType.Color,
                    Float("r"), Float("g"), Float("b"), Float("a"))))),
            Vector("m_BuildTextureStacks", "vector", N("data", "BuildTextureStackReference", SerializedPropertyType.Generic,
                Str("groupName"), Str("itemName"))),
            Hidden(N("m_AllowLocking", "bool", SerializedPropertyType.Boolean)),
        };
    }
}

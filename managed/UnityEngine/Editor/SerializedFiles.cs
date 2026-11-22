using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>EditorJsonUtility: JsonUtility's format for any object, engine objects included,
    /// with references to assets by file id and GUID (they survive the session).</summary>
    public static partial class EditorJsonUtility
    {
        public static string ToJson(object obj) => ToJson(obj, false);

        public static string ToJson(object obj, bool prettyPrint) => JsonUtility.EditorToJson(obj, prettyPrint);

        public static void FromJsonOverwrite(string json, object objectToOverwrite) =>
            JsonUtility.EditorFromJsonOverwrite(json, objectToOverwrite);
    }
}

namespace UnityEditorInternal
{
    public partial class InternalEditorUtility
    {
        /// <summary>Unbound's Editor is interactive (it has the game window), not a batch-mode one.</summary>
        [Unbound.EditorFaithful("as the interactive Editor (tests/projects/editor lists it as an expected difference from the batch-mode reference)")]
        public static bool inBatchMode => false;

        [Unbound.EditorFaithful("as the interactive Editor (tests/projects/editor lists it as an expected difference from the batch-mode reference)")]
        public static bool isHumanControllingUs => true;

        public static bool CurrentThreadIsMainThread() => PlayerLoopDriver.IsMainThread;

        /// <summary>
        /// Reads the objects of a serialized file (Unity's text format), not as assets: editor
        /// singletons and settings load their state so.
        /// </summary>
        public static Object[] LoadSerializedFileAndForget(string path)
        {
            if (string.IsNullOrEmpty(path)) return Array.Empty<Object>();
            var file = SerializedFiles.ReadablePath(path);
            if (file == null) return Array.Empty<Object>();
            List<YDocument> documents;
            try
            {
                documents = Native.LoadDocuments(file);
            }
            catch (Exception e)
            {
                Debug.LogError($"Unbound: {path} cannot be read ({e.Message}); only Unity's text serialization is supported.");
                return Array.Empty<Object>();
            }
            var objects = new List<Object>();
            var otherDocuments = new List<YDocument>();
            foreach (var doc in documents)
            {
                if (doc.ClassId == UnityEditor.U2D.SpriteAtlasAsset.ClassId)
                {
                    var asset = new UnityEditor.U2D.SpriteAtlasAsset(fromDeserialization: true);
                    asset.ReadSerialized(doc.Body);
                    asset.IsPersistentAsset = false;
                    objects.Add(asset);
                }
                else
                {
                    otherDocuments.Add(doc);
                }
            }
            if (otherDocuments.Count > 0)
            {
                var loaded = ObjectGraphBuilder.Build(otherDocuments, path, null, ObjectGraphBuilder.Kind.Asset, null);
                foreach (var o in loaded.Objects.Values)
                {
                    o.IsPersistentAsset = false;
                    objects.Add(o);
                }
            }
            return objects.ToArray();
        }

        /// <summary>
        /// Writes objects in Unity's text serialization to the path, as the Editor does (a file
        /// of the Editor's own preferences goes to Unbound's copy, <see cref="SerializedFiles"/>).
        /// </summary>
        public static void SaveToSerializedFileAndForget(Object[] obj, string path, bool allowTextSerialization)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            foreach (var o in obj)
                if (o != null) EditorUtility.ClearDirty(o);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError("File couldn't be written because path is empty.");
                return;
            }
            var target = SerializedFiles.WritablePath(path, out bool isOverlay);
            var dir = Path.GetDirectoryName(target);
            if (isOverlay)
            {
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            }
            else if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Debug.LogError($"File {path} couldn't be written because its directory does not exist.");
                return;
            }
            File.WriteAllText(target, UnityYamlWriter.Write(obj));
        }
    }
}

namespace Unbound.Runtime
{
    /// <summary>Where editor files live: project-relative paths are the project's, written there as
    /// the Editor writes them; files of the installed Editor's preferences (shared by every
    /// project and Editor on the machine) go to Unbound's copy in the global cache, read first.</summary>
    internal static class SerializedFiles
    {
        static string Project => ProjectInfo.ProjectPath;

        static string Absolute(string path) =>
            Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(Project, path));

        static string Overlay(string absolute)
        {
            var preferences = Path.GetFullPath(UnityEditorInternal.InternalEditorUtility.unityPreferencesFolder).TrimEnd('/') + "/";
            if (absolute.StartsWith(preferences, StringComparison.Ordinal))
            {
                var cache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
                if (string.IsNullOrEmpty(cache)) cache = Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "", ".cache");
                return Path.Combine(cache, "unbound", "EditorPreferences", absolute.Substring(preferences.Length));
            }
            return null;
        }

        internal static string WritablePath(string path, out bool isOverlay)
        {
            var absolute = Absolute(path);
            var overlay = Overlay(absolute);
            isOverlay = overlay != null;
            return overlay ?? absolute;
        }

        internal static string WritablePath(string path) => WritablePath(path, out _);

        /// <summary>The file to read for a path (Unbound's copy first); null when there is none.</summary>
        internal static string ReadablePath(string path)
        {
            var absolute = Absolute(path);
            var overlay = Overlay(absolute);
            if (overlay != null && File.Exists(overlay)) return overlay;
            return File.Exists(absolute) ? absolute : null;
        }
    }

    /// <summary>
    /// Unity's text serialization of script objects (MonoBehaviour, ScriptableObject): the
    /// document per object, the native header fields, then the serialized fields in Unity's YAML
    /// forms (flow maps for math structs and references, block maps and sequences otherwise).
    /// </summary>
    internal static partial class UnityYamlWriter
    {
        internal static string Write(Object[] objects)
        {
            var ids = new Dictionary<Object, long>(ReferenceEqualityComparer.Instance);
            long next = 1;
            foreach (var o in objects)
                if (o != null && !ids.ContainsKey(o)) ids[o] = next++;
            return Write(objects, ids);
        }

        /// <summary>The objects' documents in the given order, each under its file id in
        /// <paramref name="ids"/> (references between them by those ids).</summary>
        internal static string Write(Object[] objects, Dictionary<Object, long> ids)
        {
            var sb = new StringBuilder("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            foreach (var o in objects)
            {
                if (o == null) continue;
                var writer = new Writer(sb, ids);
                if (o is UnityEditor.U2D.SpriteAtlasAsset atlasAsset)
                {
                    sb.Append("--- !u!").Append(UnityEditor.U2D.SpriteAtlasAsset.ClassId).Append(" &").Append(ids[o]).Append('\n');
                    sb.Append("SpriteAtlasAsset:\n");
                    atlasAsset.WriteYaml(writer);
                    continue;
                }
                if (o is Material material)
                {
                    var doc = new YTextDocument
                    {
                        Header = "--- !u!" + EngineTypes.EngineClassIdOf(typeof(Material)) + " &" + ids[o],
                        ClassName = "Material",
                        Body = MaterialTypeTree.NewFileBody(material, r => SceneFiles.FlowValue(writer.ReferenceText(r))),
                    };
                    var text = UnityYamlText.Write(new List<YTextDocument> { doc });
                    sb.Append(text, UnityYamlText.Preamble.Length, text.Length - UnityYamlText.Preamble.Length);
                    continue;
                }
                if (o is Mesh mesh)
                {
                    sb.Append("--- !u!").Append(EngineTypes.EngineClassIdOf(typeof(Mesh))).Append(" &").Append(ids[o]).Append('\n');
                    sb.Append("Mesh:\n");
                    writer.MeshBody(mesh);
                    continue;
                }
                if (o is Texture2D texture)
                {
                    sb.Append("--- !u!").Append(EngineTypes.EngineClassIdOf(typeof(Texture2D))).Append(" &").Append(ids[o]).Append('\n');
                    sb.Append("Texture2D:\n");
                    writer.Texture2DBody(texture);
                    continue;
                }
                if (ControllerFiles.Writes(o))
                {
                    sb.Append("--- !u!").Append(EngineTypes.EngineClassIdOf(o.GetType())).Append(" &").Append(ids[o]).Append('\n');
                    sb.Append(o.GetType().Name).Append(":\n");
                    switch (o)
                    {
                        case UnityEditor.Animations.AnimatorController c: writer.AnimatorControllerBody(c); break;
                        case UnityEditor.Animations.AnimatorStateMachine m: writer.AnimatorStateMachineBody(m); break;
                        case UnityEditor.Animations.AnimatorState s: writer.AnimatorStateBody(s); break;
                        case UnityEditor.Animations.AnimatorTransitionBase t: writer.AnimatorTransitionBody(t); break;
                        case UnityEditor.Animations.BlendTree b: writer.BlendTreeBody(b); break;
                    }
                    continue;
                }
                if (o is AnimationClip clip)
                {
                    sb.Append("--- !u!").Append(EngineTypes.EngineClassIdOf(typeof(AnimationClip))).Append(" &").Append(ids[o]).Append('\n');
                    sb.Append("AnimationClip:\n");
                    writer.AnimationClipBody(clip);
                    continue;
                }
                if (o is PhysicsMaterial physics)
                {
                    sb.Append("--- !u!").Append(EngineTypes.EngineClassIdOf(typeof(PhysicsMaterial))).Append(" &").Append(ids[o]).Append('\n');
                    sb.Append("PhysicsMaterial:\n");
                    writer.PhysicsMaterialBody(physics);
                    continue;
                }
                if (!SerializedTree.IsScript(o))
                    throw new NotSupportedException($"Unbound writes script objects, materials and physics materials only; {o.GetType().Name} '{o.name}' cannot be saved.");
                sb.Append("--- !u!114 &").Append(ids[o]).Append('\n');
                sb.Append("MonoBehaviour:\n");
                writer.ScriptBody(o);
            }
            return sb.ToString();
        }

        /// <summary>Whether <see cref="Write(Object[], Dictionary{Object, long})"/> writes the
        /// object whole into a new file (not a material variant: unmeasured).</summary>
        internal static bool WritesNew(Object o) => SerializedTree.IsScript(o) || o is Material { isVariant: false } || o is PhysicsMaterial
            || o is AnimationClip clip && AnimationClipFiles.Unwritable(clip) == null
            || ControllerFiles.Writes(o)
            || o is Texture2D { isReadable: true } && !UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsCompressedFormat(((Texture2D)o).format)
            || o is Mesh { isReadable: true } && ((Mesh)o).m_MeshCompression == UnityEditor.ModelImporterMeshCompression.Off;

        // Not written (unmeasured): compressed or unreadable meshes, unreadable and block-compressed
        // or crunched textures.

        /// <summary>Writes one document's fields. References to objects of the same file are by
        /// <c>ids</c>; to assets in <c>assets</c> (an import's own meshes and materials) by their GUID
        /// and file id; to other assets by where the asset database has them.</summary>
        internal sealed partial class Writer
        {
            StringBuilder m_Out;
            readonly Dictionary<Object, long> m_Ids;
            readonly Dictionary<Object, (string Guid, long FileId)> m_Assets;
            readonly Dictionary<Object, string> m_Located;

            /// <summary>Overrides how a reference is written (null: as usual).</summary>
            readonly Func<Object, string> m_References;
            /// <summary>Where assets are, for a save (null: looked up per reference).</summary>
            readonly AssetDatabaseRuntime.Locations m_Locations;

            /// <summary>What a reference to an asset Unbound cannot locate is written as when
            /// <see cref="m_Locations"/> is given: a save then keeps the file's reference
            /// (<see cref="SceneFiles.Overlay"/>).</summary>
            internal const string UnlocatedReference = "{fileID: unlocated}";

            /// <summary><paramref name="located"/>: references to other files' objects found
            /// before, kept for writers that write many documents.</summary>
            internal Writer(StringBuilder output, Dictionary<Object, long> ids, Dictionary<Object, (string Guid, long FileId)> assets = null,
                Dictionary<Object, string> located = null)
                : this(output, ids, assets, located, null, null) { }

            /// <summary>A save's writer: <paramref name="references"/> overrides how references are
            /// written, <paramref name="locations"/> names assets.</summary>
            internal Writer(StringBuilder output, Dictionary<Object, long> ids, Dictionary<Object, (string Guid, long FileId)> assets,
                Func<Object, string> references, AssetDatabaseRuntime.Locations locations)
                : this(output, ids, assets, null, references, locations) { }

            Writer(StringBuilder output, Dictionary<Object, long> ids, Dictionary<Object, (string Guid, long FileId)> assets,
                Dictionary<Object, string> located, Func<Object, string> references, AssetDatabaseRuntime.Locations locations)
            {
                m_Out = output;
                m_Ids = ids;
                m_Assets = assets;
                m_Located = located;
                m_References = references;
                m_Locations = locations;
            }

            void Indent(int level) => m_Out.Append(' ', level * 2);

            // Typed fields of native objects, at the document's top level unless a level is given.
            internal void Float(string key, float value, int level = 1) => Line(level, key, F(value));
            internal void Int(string key, long value, int level = 1) => Line(level, key, value.ToString(CultureInfo.InvariantCulture));
            internal void Bool(string key, bool value, int level = 1) => Line(level, key, value ? "1" : "0");
            internal void Vector3(string key, Vector3 value, int level = 1) => Line(level, key, Flow(value, typeof(Vector3), Shape.Vector3));
            internal void Quaternion(string key, Quaternion value, int level = 1) => Line(level, key, Flow(value, typeof(Quaternion), Shape.Quaternion));
            internal void Vector2(string key, Vector2 value, int level = 1) => Line(level, key, Flow(value, typeof(Vector2), Shape.Vector2));
            internal void Color(string key, Color value, int level = 1) => Line(level, key, Flow(value, typeof(Color), Shape.Color));
            internal void Vector4(string key, Vector4 value, int level = 1) => Line(level, key, Flow(value, typeof(Vector4), Shape.Vector4));
            internal void Matrix(string key, Matrix4x4 value, int level = 1)
            {
                Key(key, level);
                Block(value, Shape.Matrix4x4, level + 1);
            }
            internal void Rect(string key, Rect value, int level = 1)
            {
                Key(key, level);
                Block(value, Shape.Rect, level + 1);
            }
            internal void Ref(string key, Object value, int level = 1) => Line(level, key, Reference(value));
            internal void Mask(string key, int bits, int level = 1)
            {
                Indent(level);
                m_Out.Append(key).Append(":\n");
                Line(level + 1, "serializedVersion", "2");
                Line(level + 1, "m_Bits", ((uint)bits).ToString(CultureInfo.InvariantCulture));
            }

            /// <summary>A list of references (<c>m_Materials</c>, <c>m_Children</c>): flow items at
            /// the key's indentation.</summary>
            internal void RefList<T>(string key, IEnumerable<T> values, int level = 1) where T : Object
            {
                int start = m_Out.Length;
                Indent(level);
                m_Out.Append(key).Append(":\n");
                int items = 0;
                foreach (var value in values)
                {
                    Indent(level);
                    m_Out.Append("- ").Append(Reference(value)).Append('\n');
                    items++;
                }
                if (items == 0)
                {
                    m_Out.Length = start;
                    Line(level, key, "[]");
                }
            }

            /// <summary>A sequence item's first field, the dash at the key's indentation
            /// (<c>- key: value</c>); its further fields go at <c>level + 1</c>.</summary>
            /// <summary>A list of scalars written as they are given (numbers already formatted).</summary>
            internal void Scalars(string key, IEnumerable<string> values, int level = 1)
            {
                int start = m_Out.Length;
                Indent(level);
                m_Out.Append(key).Append(":\n");
                int items = 0;
                foreach (var value in values)
                {
                    Indent(level);
                    m_Out.Append("- ").Append(value).Append('\n');
                    items++;
                }
                if (items == 0)
                {
                    m_Out.Length = start;
                    Line(level, key, "[]");
                }
            }

            internal void Item(string key, string value, int level)
            {
                Indent(level);
                m_Out.Append("- ").Append(key).Append(':');
                m_Out.Append(value.Length > 0 ? " " + value : " ").Append('\n');
            }

            internal void Key(string key, int level)
            {
                Indent(level);
                m_Out.Append(key).Append(":\n");
            }

            internal static string Number(float value) => F(value);
            internal string ReferenceText(Object value) => Reference(value);

            /// <summary>A reference kept as read (an asset Unbound does not load).</summary>
            internal static string ReferenceText(ObjectRef value) =>
                value.IsNull ? "{fileID: 0}"
                : value.Guid == null ? $"{{fileID: {value.FileId}}}"
                : $"{{fileID: {value.FileId}, guid: {value.Guid}, type: {value.Type}}}";

            /// <summary>The fields every object's document starts with: its hide flags and no prefab links.</summary>
            void ObjectHeader(Object o)
            {
                Line(1, "m_ObjectHideFlags", ((uint)o.HideFlagsNoThrow).ToString(CultureInfo.InvariantCulture));
                Line(1, "m_CorrespondingSourceObject", "{fileID: 0}");
                Line(1, "m_PrefabInstance", "{fileID: 0}");
                Line(1, "m_PrefabAsset", "{fileID: 0}");
            }

            internal void Header(Object o)
            {
                ObjectHeader(o);
                Line(1, "m_GameObject", o is Component c ? Reference(c.gameObject) : "{fileID: 0}");
                Line(1, "m_Enabled", o is Behaviour b && !b.enabled ? "0" : "1");
                Line(1, "m_EditorHideFlags", "0");
                Line(1, "m_Script", Reference(MonoScript.FromType(o.GetType())));
                // A component's document names nothing (its GameObject has the name).
                String("m_Name", o is Component ? "" : o.name);
                Line(1, "m_EditorClassIdentifier", "");
            }

            /// <summary>A physics material's whole body (matches the Unity 6000.0.71f1 Editor,
            /// tests/projects/scenesave create-assets Bouncy.physicMaterial).</summary>
            internal void PhysicsMaterialBody(PhysicsMaterial m)
            {
                ObjectHeader(m);
                String("m_Name", m.name);
                Int("serializedVersion", PhysicsMaterialSerializedVersion);
                Float("m_DynamicFriction", m.dynamicFriction);
                Float("m_StaticFriction", m.staticFriction);
                Float("m_Bounciness", m.bounciness);
                Int("m_FrictionCombine", (int)m.frictionCombine);
                Int("m_BounceCombine", (int)m.bounceCombine);
            }

            /// <summary>A readable texture's whole body: its size, format, mip count and sampler
            /// settings and its raw data, every mip (matches the Unity 6000.0.71f1 Editor,
            /// tests/projects/scenesave engine-assets Mipped, Linear, Alpha, create-assets Tex).</summary>
            internal void Texture2DBody(Texture2D t)
            {
                var data = t.GetRawTextureData();
                ObjectHeader(t);
                String("m_Name", t.name);
                Key("m_ImageContentsHash", 1);
                Int("serializedVersion", 2, 2);
                Line(2, "Hash", NoImageContentsHash);
                Bool("m_IsAlphaChannelOptional", false);
                Int("serializedVersion", Texture2DSerializedVersion);
                Int("m_Width", t.width);
                Int("m_Height", t.height);
                Int("m_CompleteImageSize", data.Length);
                Int("m_MipsStripped", 0);
                Int("m_TextureFormat", (int)t.format);
                Int("m_MipCount", t.mipmapCount);
                Bool("m_IsReadable", t.isReadable);
                Bool("m_IsPreProcessed", false);
                Bool("m_IgnoreMipmapLimit", t.ignoreMipmapLimit);
                Line(1, "m_MipmapLimitGroupName", "");
                Bool("m_StreamingMipmaps", false);
                Int("m_StreamingMipmapsPriority", 0);
                Bool("m_VTOnly", false);
                Bool("m_AlphaIsTransparency", t.alphaIsTransparency);
                Int("m_ImageCount", 1);
                Int("m_TextureDimension", (int)UnityEngine.Rendering.TextureDimension.Tex2D);
                Key("m_TextureSettings", 1);
                Int("serializedVersion", 2, 2);
                Int("m_FilterMode", (int)t.filterMode, 2);
                Int("m_Aniso", t.anisoLevel, 2);
                Float("m_MipBias", t.mipMapBias, 2);
                Int("m_WrapU", (int)t.wrapModeU, 2);
                Int("m_WrapV", (int)t.wrapModeV, 2);
                Int("m_WrapW", (int)t.wrapModeW, 2);
                Int("m_LightmapFormat", 0);
                // The color space the data is in: 1 for sRGB, 0 for a linear texture.
                Int("m_ColorSpace", t.m_Linear ? 0 : 1);
                Line(1, "m_PlatformBlob", "");
                Int("image data", data.Length);
                Line(1, "_typelessdata", Convert.ToHexString(data).ToLowerInvariant());
                StreamData();
            }

            /// <summary>A texture's or mesh's data kept in the file itself (no streaming file).</summary>
            void StreamData()
            {
                Key("m_StreamData", 1);
                Int("serializedVersion", 2, 2);
                Int("offset", 0, 2);
                Int("size", 0, 2);
                Line(2, "path", "");
            }

            /// <summary>The image contents hash of a texture made by script: none.</summary>
            const string NoImageContentsHash = "00000000000000000000000000000000";

            /// <summary>The texture's serialized version in the release's files.</summary>
            const int Texture2DSerializedVersion = 3;

            /// <summary>The physics material's serialized version in the release's files.</summary>
            const int PhysicsMaterialSerializedVersion = 2;

            /// <summary>A script object's whole body: the header, its serialized fields (after its
            /// OnBeforeSerialize) and the managed references they hold.</summary>
            internal void ScriptBody(Object o)
            {
                Header(o);
                BeforeSerialize(o);
                Fields(o, o.GetType(), 1);
                References(1);
            }

            static void BeforeSerialize(object o)
            {
                if (o is not ISerializationCallbackReceiver receiver) return;
                try { receiver.OnBeforeSerialize(); }
                catch (Exception e) { Debug.LogException(e); }
            }

            internal void SequenceValue(int level, string value)
            {
                Indent(level);
                m_Out.Append("- ").Append(value).Append('\n');
            }

            internal void Line(int level, string key, string value)
            {
                Indent(level);
                m_Out.Append(key).Append(':');
                if (value.Length > 0) m_Out.Append(' ').Append(value);
                else m_Out.Append(' ');
                m_Out.Append('\n');
            }

            // ------------------------------------------------------------ managed references

            /// <summary>The managed references of the document being written, by object, in the order
            /// they were first written; whether a null one was (the reserved id -2); whether the
            /// document has [SerializeReference] fields at all (it then gets a references block).</summary>
            readonly Dictionary<object, long> m_Rids = new Dictionary<object, long>(ReferenceEqualityComparer.Instance);
            readonly List<object> m_Referenced = new List<object>();
            bool m_NullReference, m_HasManagedReferences;

            string Rid(object value)
            {
                m_HasManagedReferences = true;
                if (value == null)
                {
                    m_NullReference = true;
                    return ManagedReferenceIds.Null.ToString(CultureInfo.InvariantCulture);
                }
                if (!m_Rids.TryGetValue(value, out var rid))
                {
                    rid = ManagedReferenceIds.For(value, m_Rids.Values);
                    m_Rids[value] = rid;
                    m_Referenced.Add(value);
                }
                return rid.ToString(CultureInfo.InvariantCulture);
            }

            /// <summary>The document's references block (Unity 2021.2+ version 2), when it has
            /// [SerializeReference] fields: the null entry, then each referenced object by
            /// ascending id with its type and data (written first, so references found inside the
            /// data are listed too).</summary>
            internal void References(int level)
            {
                if (!m_HasManagedReferences) return;
                var data = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
                var outer = m_Out;
                for (int i = 0; i < m_Referenced.Count; i++)
                {
                    var o = m_Referenced[i];
                    var sb = new StringBuilder();
                    m_Out = sb;
                    BeforeSerialize(o);
                    Fields(o, o.GetType(), level + 3);
                    data[o] = sb.ToString();
                }
                m_Out = outer;
                Key("references", level);
                Line(level + 1, "version", "2");
                if (!m_NullReference && m_Referenced.Count == 0)
                {
                    Line(level + 1, "RefIds", "[]");
                    return;
                }
                Key("RefIds", level + 1);
                if (m_NullReference)
                {
                    Item("rid", ManagedReferenceIds.Null.ToString(CultureInfo.InvariantCulture), level + 1);
                    Line(level + 2, "type", "{class: , ns: , asm: }");
                }
                var ordered = new List<object>(m_Referenced);
                ordered.Sort((a, b) => m_Rids[a].CompareTo(m_Rids[b]));
                foreach (var o in ordered)
                {
                    Item("rid", m_Rids[o].ToString(CultureInfo.InvariantCulture), level + 1);
                    Line(level + 2, "type", ManagedReferenceIds.TypeText(o.GetType()));
                    if (data[o].Length == 0) Line(level + 2, "data", "{}");
                    else
                    {
                        Key("data", level + 2);
                        m_Out.Append(data[o]);
                    }
                }
            }

            // ------------------------------------------------------------ fields

            internal void Fields(object obj, Type type, int level, bool guiStatePayload = false)
            {
                foreach (var member in SerializationRules.Members(type, guiStatePayload))
                {
                    var value = member.Field.GetValue(obj);
                    if (member.Shape == Shape.ManagedReference)
                    {
                        Key(member.Name, level);
                        Line(level + 1, "rid", Rid(value));
                        continue;
                    }
                    Member(member.Name, value, member.Field.FieldType, member.Shape, member.ElementType, member.ElementShape, level);
                }
            }

            /// <summary>Arrays of these are written as one hex string of the elements' little-endian
            /// bytes; arrays of floats, doubles and everything else as sequences (measured:
            /// tests/projects/scenesave).</summary>
            static bool IsHexElement(Shape shape) =>
                shape is Shape.Bool or Shape.SByte or Shape.Byte or Shape.Short or Shape.UShort or Shape.Int
                    or Shape.UInt or Shape.Long or Shape.ULong or Shape.Char or Shape.Enum;

            static string Hex(IList list, Type elementType)
            {
                var sb = new StringBuilder();
                if (list == null) return "";
                var stored = elementType.IsEnum ? Enum.GetUnderlyingType(elementType) : elementType;
                foreach (var item in list)
                {
                    ulong bits;
                    int size;
                    switch (item)
                    {
                        case bool b: bits = b ? 1UL : 0UL; size = 1; break;
                        case char ch: bits = ch; size = 2; break;
                        default:
                            var raw = elementType.IsEnum ? Convert.ChangeType(item, stored, CultureInfo.InvariantCulture) : item;
                            size = System.Runtime.InteropServices.Marshal.SizeOf(stored);
                            bits = raw switch
                            {
                                sbyte v => (byte)v, byte v => v, short v => (ushort)v, ushort v => v,
                                int v => (uint)v, uint v => v, long v => (ulong)v, ulong v => v,
                                _ => throw new NotSupportedException($"Unbound does not write {elementType.Name} arrays."),
                            };
                            break;
                    }
                    for (int i = 0; i < size; i++) sb.Append(((byte)(bits >> (8 * i))).ToString("x2", CultureInfo.InvariantCulture));
                }
                return sb.ToString();
            }

            void Member(string name, object value, Type type, Shape shape, Type elementType, Shape elementShape, int level)
            {
                if (shape == Shape.Array || shape == Shape.List)
                {
                    var list = value as IList;
                    if (elementShape == Shape.ManagedReference) m_HasManagedReferences = true;
                    if (IsHexElement(elementShape))
                    {
                        Line(level, name, Hex(list, elementType));
                        return;
                    }
                    if (list == null || list.Count == 0)
                    {
                        Line(level, name, "[]");
                        return;
                    }
                    Indent(level);
                    m_Out.Append(name).Append(":\n");
                    foreach (var item in list) Element(item, elementType, elementShape, level);
                    return;
                }
                if (SerializationRules.IsComposite(shape))
                {
                    var composite = value ?? SerializationRules.Create(type);
                    if (SerializationRules.Members(composite.GetType(), shape == Shape.GuiStyleState).Length == 0)
                    {
                        Line(level, name, "{}");
                        return;
                    }
                    Indent(level);
                    m_Out.Append(name).Append(":\n");
                    BeforeSerialize(composite);
                    Fields(composite, composite.GetType(), level + 1, shape == Shape.GuiStyleState);
                    return;
                }
                if (shape == Shape.String)
                {
                    String(name, (string)value, level);
                    return;
                }
                if (IsBlock(shape))
                {
                    Key(name, level);
                    Block(value, shape, level + 1);
                    return;
                }
                Line(level, name, Flow(value, type, shape));
            }

            /// <summary>A sequence item, at the parent key's indentation ("- value").</summary>
            void Element(object item, Type type, Shape shape, int level)
            {
                if (shape == Shape.ManagedReference)
                {
                    Item("rid", Rid(item), level);
                    return;
                }
                if (SerializationRules.IsComposite(shape))
                {
                    var composite = item ?? SerializationRules.Create(type);
                    var start = m_Out.Length;
                    BeforeSerialize(composite);
                    Fields(composite, composite.GetType(), level + 1, shape == Shape.GuiStyleState);
                    if (m_Out.Length == start)
                    {
                        Indent(level);
                        m_Out.Append("- {}\n");
                        return;
                    }
                    Dash(start, level);
                    return;
                }
                if (IsBlock(shape))
                {
                    var start = m_Out.Length;
                    Block(item, shape, level + 1);
                    Dash(start, level);
                    return;
                }
                Indent(level);
                m_Out.Append('-');
                var text = shape == Shape.String
                    ? Scalar((string)item, level * 2 + 2, (level + 1) * 2)
                    : Flow(item, type, shape);
                m_Out.Append(' ').Append(text).Append('\n');
            }

            /// <summary>A sequence item's first line, written one level deeper: its indentation becomes the dash.</summary>
            void Dash(int start, int level)
            {
                m_Out.Remove(start, level * 2 + 2);
                m_Out.Insert(start, new string(' ', level * 2) + "- ");
            }

            /// <summary>A string field: plain, quoted or over several lines as the Editor writes it at
            /// this place (<see cref="Scalar(string, int, int)"/>).</summary>
            internal void String(string key, string value, int level = 1) =>
                Line(level, key, Scalar(value, level * 2 + key.Length + 2, (level + 1) * 2));

            /// <summary>The spherical harmonics coefficient keys, quoted as Unity writes them.</summary>
            static readonly string[] s_ShKeys = Array.ConvertAll(UnityEngine.Rendering.SphericalHarmonicsL2.SerializedNames, n => "'" + n + "'");

            /// <summary>Values Unity writes as block maps of their own fields.</summary>
            static bool IsBlock(Shape shape) =>
                shape is Shape.Rect or Shape.LayerMask or Shape.RenderingLayerMask or Shape.SphericalHarmonicsL2
                    or Shape.Bounds or Shape.Hash128 or Shape.Color32 or Shape.Matrix4x4 or Shape.RectOffset
                    or Shape.AnimationCurve or Shape.Gradient;

            /// <summary>A block value's fields at <paramref name="level"/> (the layouts measured in
            /// tests/projects/scenesave and the native documents of the same types).</summary>
            void Block(object value, Shape shape, int level)
            {
                switch (shape)
                {
                    case Shape.Rect:
                    {
                        var r = (Rect)value;
                        Line(level, "serializedVersion", "2");
                        Line(level, "x", F(r.x));
                        Line(level, "y", F(r.y));
                        Line(level, "width", F(r.width));
                        Line(level, "height", F(r.height));
                        return;
                    }
                    case Shape.LayerMask:
                        Line(level, "serializedVersion", "2");
                        Line(level, "m_Bits", ((uint)((LayerMask)value).value).ToString(CultureInfo.InvariantCulture));
                        return;
                    case Shape.RenderingLayerMask:
                        Line(level, "serializedVersion", "0");
                        Line(level, "m_Bits", ((RenderingLayerMask)value).value.ToString(CultureInfo.InvariantCulture));
                        return;
                    case Shape.SphericalHarmonicsL2:
                    {
                        var sh = (UnityEngine.Rendering.SphericalHarmonicsL2)value;
                        for (int i = 0; i < s_ShKeys.Length; i++) Line(level, s_ShKeys[i], F(sh.Coefficient(i)));
                        return;
                    }
                    case Shape.Bounds:
                    {
                        var b = (Bounds)value;
                        Line(level, "m_Center", Flow(b.center, typeof(Vector3), Shape.Vector3));
                        Line(level, "m_Extent", Flow(b.extents, typeof(Vector3), Shape.Vector3));
                        return;
                    }
                    case Shape.Hash128:
                        Line(level, "serializedVersion", "2");
                        Line(level, "Hash", ((Hash128)value).ToString());
                        return;
                    case Shape.Color32:
                    {
                        var c = (Color32)value;
                        Line(level, "serializedVersion", "2");
                        Line(level, "rgba", ((uint)(c.r | c.g << 8 | c.b << 16 | c.a << 24)).ToString(CultureInfo.InvariantCulture));
                        return;
                    }
                    case Shape.Matrix4x4:
                    {
                        var m = (Matrix4x4)value;
                        for (int r = 0; r < 4; r++)
                            for (int c = 0; c < 4; c++)
                                Line(level, "e" + r + c, F(m[r, c]));
                        return;
                    }
                    case Shape.RectOffset:
                    {
                        var o = (RectOffset)value ?? new RectOffset();
                        Line(level, "m_Left", o.left.ToString(CultureInfo.InvariantCulture));
                        Line(level, "m_Right", o.right.ToString(CultureInfo.InvariantCulture));
                        Line(level, "m_Top", o.top.ToString(CultureInfo.InvariantCulture));
                        Line(level, "m_Bottom", o.bottom.ToString(CultureInfo.InvariantCulture));
                        return;
                    }
                    case Shape.AnimationCurve:
                        Curve((AnimationCurve)value ?? new AnimationCurve(), level);
                        return;
                    case Shape.Gradient:
                        GradientBlock((Gradient)value ?? new Gradient(), level);
                        return;
                }
            }

            /// <summary>A curve's infinity modes as stored (the reader's inverse): 0 ping-pong, 1 loop,
            /// 2 clamp.</summary>
            static int Infinity(WrapMode mode) => mode == WrapMode.PingPong ? 0 : mode == WrapMode.Loop ? 1 : 2;

            void Curve(AnimationCurve curve, int level)
            {
                Line(level, "serializedVersion", "2");
                var keys = curve.keys;
                if (keys.Length == 0) Line(level, "m_Curve", "[]");
                else
                {
                    Key("m_Curve", level);
                    foreach (var k in keys)
                    {
                        Item("serializedVersion", "3", level);
                        Line(level + 1, "time", F(k.time));
                        Line(level + 1, "value", F(k.value));
                        Line(level + 1, "inSlope", F(k.inTangent));
                        Line(level + 1, "outSlope", F(k.outTangent));
#pragma warning disable CS0618
                        Line(level + 1, "tangentMode", k.tangentMode.ToString(CultureInfo.InvariantCulture));
#pragma warning restore CS0618
                        Line(level + 1, "weightedMode", ((int)k.weightedMode).ToString(CultureInfo.InvariantCulture));
                        Line(level + 1, "inWeight", F(k.inWeight));
                        Line(level + 1, "outWeight", F(k.outWeight));
                    }
                }
                Line(level, "m_PreInfinity", Infinity(curve.preWrapMode).ToString(CultureInfo.InvariantCulture));
                Line(level, "m_PostInfinity", Infinity(curve.postWrapMode).ToString(CultureInfo.InvariantCulture));
                Line(level, "m_RotationOrder", "4");
            }

            /// <summary>The number of key slots a gradient stores, and the scale of its key times.</summary>
            const int GradientSlots = 8;
            const float GradientTimeScale = 65535f;

            void GradientBlock(Gradient g, int level)
            {
                var colors = g.colorKeys;
                var alphas = g.alphaKeys;
                Line(level, "serializedVersion", "2");
                for (int i = 0; i < GradientSlots; i++)
                {
                    var rgb = i < colors.Length ? colors[i].color : default;
                    float a = i < alphas.Length ? alphas[i].alpha : 0f;
                    Line(level, "key" + i, Flow(new Color(rgb.r, rgb.g, rgb.b, a), typeof(Color), Shape.Color));
                }
                for (int i = 0; i < GradientSlots; i++)
                    Line(level, "ctime" + i, (i < colors.Length ? KeyTime(colors[i].time) : 0).ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < GradientSlots; i++)
                    Line(level, "atime" + i, (i < alphas.Length ? KeyTime(alphas[i].time) : 0).ToString(CultureInfo.InvariantCulture));
                Line(level, "m_Mode", ((int)g.mode).ToString(CultureInfo.InvariantCulture));
                Line(level, "m_ColorSpace", ((int)g.colorSpace).ToString(CultureInfo.InvariantCulture));
                Line(level, "m_NumColorKeys", colors.Length.ToString(CultureInfo.InvariantCulture));
                Line(level, "m_NumAlphaKeys", alphas.Length.ToString(CultureInfo.InvariantCulture));
            }

            static int KeyTime(float time) => (int)Math.Round(Mathf.Clamp01(time) * GradientTimeScale);

            string Flow(object value, Type type, Shape shape)
            {
                switch (shape)
                {
                    case Shape.Bool: return (bool)value ? "1" : "0";
                    case Shape.Float: return F((float)value);
                    case Shape.Double: return D((double)value);
                    case Shape.String: return Scalar((string)value);
                    case Shape.Char: return ((int)(char)value).ToString(CultureInfo.InvariantCulture);
                    case Shape.Enum: return Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                    case Shape.SByte: case Shape.Byte: case Shape.Short: case Shape.UShort:
                    case Shape.Int: case Shape.UInt: case Shape.Long: case Shape.ULong:
                        return Convert.ToString(value, CultureInfo.InvariantCulture);
                    case Shape.UnityObject: return Reference(value as Object);
                    case Shape.LazyReference:
                    {
                        int id = LazyReferences.Id(value);
                        var target = id == 0 ? null : ObjectRegistry.FindById(id);
                        if (id != 0 && target == null)
                            throw new NotSupportedException("Unbound cannot write an unresolved LazyLoadReference without losing its target reference.");
                        return Reference(target);
                    }
                    case Shape.Vector2: { var v = (Vector2)value; return $"{{x: {F(v.x)}, y: {F(v.y)}}}"; }
                    case Shape.Vector3: { var v = (Vector3)value; return $"{{x: {F(v.x)}, y: {F(v.y)}, z: {F(v.z)}}}"; }
                    case Shape.Vector4: { var v = (Vector4)value; return $"{{x: {F(v.x)}, y: {F(v.y)}, z: {F(v.z)}, w: {F(v.w)}}}"; }
                    case Shape.Quaternion: { var v = (Quaternion)value; return $"{{x: {F(v.x)}, y: {F(v.y)}, z: {F(v.z)}, w: {F(v.w)}}}"; }
                    case Shape.Vector2Int: { var v = (Vector2Int)value; return $"{{x: {v.x}, y: {v.y}}}"; }
                    case Shape.Vector3Int: { var v = (Vector3Int)value; return $"{{x: {v.x}, y: {v.y}, z: {v.z}}}"; }
                    case Shape.Color: { var c = (Color)value; return $"{{r: {F(c.r)}, g: {F(c.g)}, b: {F(c.b)}, a: {F(c.a)}}}"; }
                    case Shape.RectInt: { var r = (RectInt)value; return $"{{x: {r.x}, y: {r.y}, width: {r.width}, height: {r.height}}}"; }
                    case Shape.BoundsInt:
                    {
                        var b = (BoundsInt)value;
                        return $"{{m_Position: {Flow(b.position, typeof(Vector3Int), Shape.Vector3Int)}, m_Size: {Flow(b.size, typeof(Vector3Int), Shape.Vector3Int)}}}";
                    }
                    case Shape.PropertyName: return Scalar(value?.ToString() ?? "");
                    default:
                        throw new NotSupportedException($"Unbound does not write {type.Name} values in Unity's text serialization yet.");
                }
            }

            string Reference(Object o)
            {
                if (o == null) return "{fileID: 0}";
                if (m_References?.Invoke(o) is string overridden) return overridden;
                if (m_Ids.TryGetValue(o, out var local)) return $"{{fileID: {local}}}";
                if (m_Assets != null && m_Assets.TryGetValue(o, out var asset)) return $"{{fileID: {asset.FileId}, guid: {asset.Guid}, type: 3}}";
                if (o is MonoScript script && script.m_Guid is string scriptGuid && scriptGuid.Length > 0)
                    return $"{{fileID: 11500000, guid: {scriptGuid}, type: 3}}";
                if (m_Locations != null)
                {
                    if (m_Locations.TryGet(o, out var at, out var atFileId, out var type))
                        return $"{{fileID: {atFileId}, guid: {at}, type: {type}}}";
                    // An object no file holds (made at runtime, another scene's) is written as none.
                    return o.IsPersistentAsset ? UnlocatedReference : "{fileID: 0}";
                }
                if (m_Located == null) return Located(o);
                if (!m_Located.TryGetValue(o, out var text)) m_Located[o] = text = Located(o);
                return text;
            }

            /// <summary>Whether a GUID is one of the Editor's built-in resource files, whose objects
            /// are referenced with type 0 (the Assets folder's GUID has the same form but is an asset).</summary>
            internal static bool IsBuiltinGuid(string guid) =>
                guid != null && guid.StartsWith("0000000000000000", StringComparison.Ordinal) && !AssetDatabase.Lists(guid);

            static string Located(Object o)
            {
                if (AssetDatabaseRuntime.TryGetLocation(o, out var guid, out var fileId))
                {
                    int kind;
                    if (IsBuiltinGuid(guid)) kind = 0;
                    else
                    {
                        try { kind = AssetDatabaseRuntime.Info(guid)?.Kind is "yaml" or "prefab" ? 2 : 3; }
                        catch (Exception) { kind = 2; }
                    }
                    return $"{{fileID: {fileId}, guid: {guid}, type: {kind}}}";
                }
                return "{fileID: 0}";
            }

            static string F(float v) => FloatText.Format(v);

            static string D(double v) => FloatText.Format(v);
        }

        /// <summary>A string as Unity's YAML writes it in a flow map or where it cannot span lines
        /// (<see cref="Scalar(string, int, int)"/>).</summary>
        internal static string Scalar(string s) => YamlScalars.Text(s, 0, 0, breaks: false);

        /// <summary>A string as Unity's YAML writes it as a block value starting at
        /// <paramref name="column"/>, continued lines at <paramref name="indent"/>.</summary>
        internal static string Scalar(string s, int column, int indent) => YamlScalars.Text(s, column, indent, breaks: true);
    }

    /// <summary>
    /// Numbers as the Unity 6000.0.71f1 Editor writes them (tests/projects/scenesave): the shortest
    /// digits that read back as the same value; plainly (<c>0.0000001</c>, <c>123456790</c>) unless
    /// the decimal exponent of the first digit is at least the type's round-trip precision (9 for a
    /// float, 17 for a double) or below its negation, then with an exponent (<c>1e+9</c>,
    /// <c>3.4028235e+38</c>, <c>1e-10</c>). The lower double bound (-17) is inferred: 1e-17 is
    /// written plainly and 1e-300 with an exponent, nothing between was measured.
    /// </summary>
    internal static class FloatText
    {
        const int FloatPrecision = 9;
        const int DoublePrecision = 17;

        internal static string Format(float v) =>
            float.IsNaN(v) ? "NaN" : float.IsInfinity(v) ? (v > 0 ? "Infinity" : "-Infinity")
            : Layout(v.ToString("R", CultureInfo.InvariantCulture), FloatPrecision);

        internal static string Format(double v) =>
            double.IsNaN(v) ? "NaN" : double.IsInfinity(v) ? (v > 0 ? "Infinity" : "-Infinity")
            : Layout(v.ToString("R", CultureInfo.InvariantCulture), DoublePrecision);

        /// <summary>Lays out the shortest round-trip text (<paramref name="shortest"/>, .NET's "R":
        /// plain or <c>d.dddE+xx</c>) by the exponent rule.</summary>
        static string Layout(string shortest, int precision)
        {
            bool negative = shortest[0] == '-';
            var body = negative ? shortest.Substring(1) : shortest;
            // Digits and the decimal exponent of the first one.
            int e = body.IndexOfAny(new[] { 'E', 'e' });
            var mantissa = e < 0 ? body : body.Substring(0, e);
            int exponent = e < 0 ? 0 : int.Parse(body.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            int dot = mantissa.IndexOf('.');
            var digits = dot < 0 ? mantissa : mantissa.Remove(dot, 1);
            int point = (dot < 0 ? mantissa.Length : dot) + exponent;
            int lead = 0;
            while (lead < digits.Length - 1 && digits[lead] == '0') { lead++; point--; }
            digits = digits.Substring(lead).TrimEnd('0');
            if (digits.Length == 0) return negative ? "-0" : "0";
            int first = point - 1;
            string text;
            if (first >= precision || first < -precision)
            {
                text = digits.Length == 1 ? digits : digits[0] + "." + digits.Substring(1);
                text += (first < 0 ? "e-" : "e+") + Math.Abs(first).ToString(CultureInfo.InvariantCulture);
            }
            else if (point <= 0) text = "0." + new string('0', -point) + digits;
            else if (point >= digits.Length) text = digits + new string('0', point - digits.Length);
            else text = digits.Substring(0, point) + "." + digits.Substring(point);
            return negative ? "-" + text : text;
        }
    }

    /// <summary>
    /// Strings as the Unity 6000.0.71f1 Editor's YAML writes them (tests/projects/scenesave
    /// texts): plainly when they read back as themselves; single-quoted when they start or end
    /// with a space, start with an indicator (<c># , [ ] { } &amp; * ! | &gt; ' " % @ `</c>, or
    /// <c>- </c>, <c>? </c>, <c>: </c> with a space after), hold <c>: </c> or end with a colon, or
    /// span lines (each line break written as an empty line, continued lines at the indentation,
    /// the closing quote right after a trailing break); double-quoted with escapes when they hold
    /// characters outside printable ASCII (tabs, carriage returns, control and non-ASCII
    /// characters: <c>\t \r \xE9 \u4E2D</c>) or a space next to a line break. Plain and
    /// single-quoted text past column 80 continues on the next line at a single space between
    /// words (libyaml's width, as the settings assets measured).
    /// </summary>
    internal static class YamlScalars
    {
        const int Width = UnityYamlText.Width;

        internal static string Text(string s, int column, int indent, bool breaks)
        {
            if (string.IsNullOrEmpty(s)) return "";
            bool special = false, lineBreaks = false, spaceBreak = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\n')
                {
                    lineBreaks = true;
                    if ((i > 0 && s[i - 1] == ' ') || (i + 1 < s.Length && s[i + 1] == ' ')) spaceBreak = true;
                }
                else if (c < 0x20 || c > 0x7e) special = true;
            }
            if (special || spaceBreak || (lineBreaks && !breaks)) return DoubleQuoted(s);
            if (!lineBreaks && Plain(s)) return breaks ? UnityYamlText.WrapPlain(s, column, indent) : s;
            return SingleQuoted(s, column, indent, breaks);
        }

        static bool Plain(string s)
        {
            if (s[0] == ' ' || s[^1] == ' ') return false;
            if ("#,[]{}&*!|>'\"%@`".IndexOf(s[0]) >= 0) return false;
            if ((s[0] == '-' || s[0] == '?' || s[0] == ':') && (s.Length == 1 || s[1] == ' ')) return false;
            for (int i = 0; i < s.Length; i++)
                if (s[i] == ':' && (i + 1 == s.Length || s[i + 1] == ' ')) return false;
            return true;
        }

        static string SingleQuoted(string s, int column, int indent, bool breaks)
        {
            var sb = new StringBuilder("'");
            column++;
            bool spaces = false, inBreaks = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == ' ')
                {
                    if (breaks && !spaces && column > Width && i != 0 && i != s.Length - 1 && s[i + 1] != ' ')
                    {
                        sb.Append('\n').Append(' ', indent);
                        column = indent;
                    }
                    else
                    {
                        sb.Append(' ');
                        column++;
                    }
                    spaces = true;
                }
                else if (c == '\n')
                {
                    if (!inBreaks) sb.Append('\n');
                    sb.Append('\n');
                    column = 0;
                    inBreaks = true;
                }
                else
                {
                    if (inBreaks)
                    {
                        sb.Append(' ', indent);
                        column = indent;
                    }
                    if (c == '\'') { sb.Append("''"); column += 2; }
                    else { sb.Append(c); column++; }
                    spaces = inBreaks = false;
                }
            }
            return sb.Append('\'').ToString();
        }

        static string DoubleQuoted(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (var c in s)
            {
                switch (c)
                {
                    case '\0': sb.Append("\\0"); break;
                    case '\a': sb.Append("\\a"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\v': sb.Append("\\v"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\x1b': sb.Append("\\e"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\u0085': sb.Append("\\N"); break;
                    case '\u00a0': sb.Append("\\_"); break;
                    case '\u2028': sb.Append("\\L"); break;
                    case '\u2029': sb.Append("\\P"); break;
                    default:
                        if (c >= 0x20 && c <= 0x7e) sb.Append(c);
                        else if (c <= 0xff) sb.Append("\\x").Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
                        else sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }

    /// <summary>
    /// The ids of managed references ([SerializeReference]): an object read from a file keeps the
    /// id it had there, so a file saved again names it alike; a new object gets a new id. The
    /// Editor derives new ids from its clock (two captures of tests/projects/scenesave differ only
    /// there); Unbound's are a per-session random base plus a counter, unique in each document.
    /// </summary>
    internal static class ManagedReferenceIds
    {
        /// <summary>The id a null reference is written with.</summary>
        internal const long Null = -2;

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, System.Runtime.CompilerServices.StrongBox<long>> s_Ids =
            new System.Runtime.CompilerServices.ConditionalWeakTable<object, System.Runtime.CompilerServices.StrongBox<long>>();
        static long s_Next = (long)((ulong)System.Random.Shared.NextInt64(1L << 46, 1L << 47) << 16);

        /// <summary>A read object's id.</summary>
        internal static void Remember(object value, long rid)
        {
            if (value == null || rid < 0) return;
            s_Ids.AddOrUpdate(value, new System.Runtime.CompilerServices.StrongBox<long>(rid));
        }

        /// <summary>The object's id in a document whose other references hold <paramref name="used"/>.</summary>
        internal static long For(object value, ICollection<long> used)
        {
            if (s_Ids.TryGetValue(value, out var known) && !used.Contains(known.Value)) return known.Value;
            long rid;
            do rid = s_Next++; while (used.Contains(rid));
            s_Ids.AddOrUpdate(value, new System.Runtime.CompilerServices.StrongBox<long>(rid));
            return rid;
        }

        /// <summary>The type entry: the class (nested classes as <c>Outer/Inner</c>), its
        /// namespace and its assembly's name.</summary>
        internal static string TypeText(Type type)
        {
            if (type.IsGenericType)
                throw new NotSupportedException($"Unbound does not write [SerializeReference] objects of generic types yet ({type.FullName}).");
            var name = type.Name;
            for (var outer = type.DeclaringType; outer != null; outer = outer.DeclaringType) name = outer.Name + "/" + name;
            var ns = type.Namespace ?? "";
            return $"{{class: {UnityYamlWriter.Scalar(name)}, ns: {UnityYamlWriter.Scalar(ns)}, asm: {UnityYamlWriter.Scalar(type.Assembly.GetName().Name)}}}";
        }
    }
}

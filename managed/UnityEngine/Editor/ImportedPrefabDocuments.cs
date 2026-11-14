using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>
    /// The documents of the prefab a ScriptedImporter makes (a SpeedTree 9 tree, a project's
    /// importer whose main object is a GameObject), in Unity's text serialization, for the native
    /// scene expansion: scene and prefab instances of the asset then expand like any prefab's, with
    /// their modifications, removed and added objects. Each GameObject of the main object's
    /// hierarchy and each of its components is a document with its file id in the import; the
    /// import's other objects (meshes, materials, wind assets) are referenced by the asset's GUID and
    /// load from the import. A component class whose fields Unbound cannot write fails the asset's
    /// instances loudly.
    /// </summary>
    internal static unsafe class ImportedPrefabDocuments
    {
        /// <summary>The native side's call (<c>imported_prefabs.rs</c>): the documents of the asset
        /// whose GUID it passes, else an error message, into its sink.</summary>
        [UnmanagedCallersOnly]
        internal static int Write(byte* guid, int length, void* sink)
        {
            string text;
            int status;
            try
            {
                text = Documents(Encoding.ASCII.GetString(guid, length));
                status = 0;
            }
            catch (Exception e)
            {
                text = e.Message;
                status = 1;
            }
            var bytes = Encoding.UTF8.GetBytes(text);
            fixed (byte* b = bytes) Native.WriteDocumentSource(sink, b, bytes.Length);
            return status;
        }

        internal static string Documents(string guid)
        {
            // Importing makes objects, which only the main thread may.
            if (!PlayerLoopDriver.IsMainThread)
                throw new InvalidOperationException("a ScriptedImporter's prefab is imported on the main thread only");
            var file = AssetDatabaseRuntime.LoadFile(guid) ?? throw new InvalidOperationException("the asset did not import");
            if (!(file.MainObject is GameObject root))
                throw new InvalidOperationException($"{file.Path}'s main object is not a GameObject, so it has no prefab to instantiate");
            return Write(file, root);
        }

        /// <summary>The documents of <paramref name="root"/>'s hierarchy, whose objects
        /// <paramref name="file"/> holds with their file ids.</summary>
        internal static string Write(LoadedFile file, GameObject root)
        {
            var ids = new Dictionary<Object, long>(ReferenceEqualityComparer.Instance);
            var assets = new Dictionary<Object, (string, long)>(ReferenceEqualityComparer.Instance);
            foreach (var pair in file.Objects)
            {
                if (pair.Value is GameObject || pair.Value is Component) ids[pair.Value] = pair.Key;
                else if (pair.Value != null) assets[pair.Value] = (file.Guid, pair.Key);
            }
            var sb = new StringBuilder("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            var writer = new UnityYamlWriter.Writer(sb, ids, assets);
            WriteGameObject(root.transform, file, ids, writer, sb);
            return sb.ToString();
        }

        static void WriteGameObject(Transform t, LoadedFile file, Dictionary<Object, long> ids, UnityYamlWriter.Writer w, StringBuilder sb)
        {
            var go = t.gameObject;
            var components = go.GetComponents<Component>();
            sb.Append("--- !u!1 &").Append(IdOf(go, file, ids)).Append("\nGameObject:\n");
            Header(w, null);
            w.Int("serializedVersion", 6);
            w.Key("m_Component", 1);
            foreach (var c in components)
                if (c != null) w.Item("component", "{fileID: " + IdOf(c, file, ids) + "}", 1);
            w.Int("m_Layer", go.m_Layer);
            w.String("m_Name", go.m_Name);
            w.String("m_TagString", go.m_Tag);
            w.Line(1, "m_Icon", "{fileID: 0}");
            w.Int("m_NavMeshLayer", go.m_NavMeshLayer);
            w.Int("m_StaticEditorFlags", go.m_StaticEditorFlags);
            w.Bool("m_IsActive", go.m_IsActive);
            foreach (var c in components)
            {
                if (c == null) continue;
                long id = IdOf(c, file, ids);
                if (c is MonoBehaviour)
                {
                    sb.Append("--- !u!114 &").Append(id).Append("\nMonoBehaviour:\n");
                    w.Header(c);
                    if (c is ISerializationCallbackReceiver receiver)
                    {
                        try { receiver.OnBeforeSerialize(); }
                        catch (Exception e) { Debug.LogException(e); }
                    }
                    w.Fields(c, c.GetType(), 1);
                    continue;
                }
                if (!Writable(c.GetType()))
                    throw new NotSupportedException($"{file.Path}: its {c.GetType().Name} components cannot be written as documents yet, so its instances in scenes and prefabs cannot expand");
                int classId = EngineTypes.EngineClassIdOf(c.GetType());
                sb.Append("--- !u!").Append(classId).Append(" &").Append(id).Append('\n').Append(c.GetType().Name).Append(":\n");
                Header(w, go);
                c.WriteSerialized(w);
            }
            for (int i = 0; i < t.childCount; i++) WriteGameObject(t.GetChild(i), file, ids, w, sb);
        }

        /// <summary>A native document's header: no prefab links (the scene's expansion adds them) and
        /// the owner of a component.</summary>
        static void Header(UnityYamlWriter.Writer w, GameObject owner)
        {
            w.Int("m_ObjectHideFlags", 0);
            w.Line(1, "m_CorrespondingSourceObject", "{fileID: 0}");
            w.Line(1, "m_PrefabInstance", "{fileID: 0}");
            w.Line(1, "m_PrefabAsset", "{fileID: 0}");
            if (owner != null) w.Ref("m_GameObject", owner);
        }

        static long IdOf(Object o, LoadedFile file, Dictionary<Object, long> ids) =>
            ids.TryGetValue(o, out var id) ? id
            : throw new InvalidOperationException($"{file.Path}: {o.GetType().Name} '{o.name}' of its main object's hierarchy has no file id");

        static readonly Dictionary<Type, bool> s_Writable = new Dictionary<Type, bool>();

        /// <summary>Whether the class itself declares its writer: a derived class (a
        /// SkinnedMeshRenderer, a RectTransform) reads fields its base's writer does not write.</summary>
        static bool Writable(Type type)
        {
            if (s_Writable.TryGetValue(type, out var writable)) return writable;
            var method = type.GetMethod(nameof(Component.WriteSerialized), BindingFlags.Instance | BindingFlags.NonPublic);
            writable = method != null && method.DeclaringType == type;
            s_Writable[type] = writable;
            return writable;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.VFX;
using UnityEngine;
using UnityEngine.VFX;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>
    /// VisualEffectResource.WriteAsset as the Unity 6000.0.71f1 Editor writes a graph file
    /// (tests/projects/vfxwrites): every document of the file is written again from its object, the
    /// resource's settings (<c>m_Infos</c>) from the resource and each of the graph's script objects
    /// whole, so a setting changed or a block's input edited changes only its lines, and a file with
    /// nothing changed keeps its bytes. An object the graph gained (a block added, with its slots)
    /// is written whole after the others, with the file id after the file's largest, in the order of
    /// the resource's contents; an object it lost loses its document (both deterministic in the
    /// Editor, tests/projects/vfxwrites block add/remove). Refused loudly (<see
    /// cref="NotSupportedException"/>, which WriteAsset reports): what Unbound cannot write.
    /// </summary>
    internal static class VisualEffectWrites
    {
        /// <summary>While AssetDatabase.SaveAssets saves: the resources whose WriteAsset refused
        /// (the package's OnWillSaveAssets wrote them first), which the save then leaves alone
        /// instead of refusing them again.</summary>
        internal static HashSet<VisualEffectResource> RefusedInSave;

        internal static void Write(VisualEffectResource resource)
        {
            if (!AssetDatabaseRuntime.TryGetLocation(resource, out var guid, out _))
            {
                Debug.LogError("Unbound: VisualEffectResource.WriteAsset: the resource is not in an asset file; nothing is written.");
                return;
            }
            var info = AssetDatabaseRuntime.Info(guid);
            var loaded = AssetDatabaseRuntime.LoadFile(guid);
            if (info == null || loaded == null || string.IsNullOrEmpty(info.File)) return;
            if (AssetDatabase.IsImmutablePackagePath(info.Path))
            {
                Debug.LogWarning($"Unbound: {info.Path} is not written: it is in a package the project does not hold.");
                return;
            }
            // Subgraph files (.vfxoperator, .vfxblock) are not measured.
            if (!info.Path.EndsWith(".vfx", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException($"Unbound does not write subgraph files yet ({info.Path}).");
            var text = File.ReadAllText(info.File);
            // A file with CRLF line endings is text serialization too (Parse reads it as LF, and it
            // is written with LF, as AssetFiles writes such a file).
            if (!text.StartsWith(UnityYamlText.Preamble, StringComparison.Ordinal)
                && !text.StartsWith(UnityYamlText.Preamble.Replace("\n", "\r\n"), StringComparison.Ordinal))
            {
                Debug.LogError($"Unbound: {info.Path} is not written: Unbound writes graph files in Unity's text serialization only.");
                return;
            }
            var docs = UnityYamlText.Parse(text);
            var ids = new Dictionary<Object, long>(ReferenceEqualityComparer.Instance);
            foreach (var p in loaded.Objects)
                if (p.Value is not null) ids[p.Value] = p.Key;

            // The graph's objects against the file's: those it gained get the file ids after the
            // file's largest, in the contents' order; those it lost are dropped.
            var inFile = new HashSet<Object>(ReferenceEqualityComparer.Instance);
            long largest = 0;
            foreach (var doc in docs)
            {
                largest = Math.Max(largest, doc.FileId);
                if (loaded.Objects.TryGetValue(doc.FileId, out var o) && o is not null) inFile.Add(o);
            }
            var gained = new List<Object>();
            foreach (var o in resource.m_Contents)
                if (o is not null && !inFile.Contains(o) && !gained.Contains(o))
                {
                    if (!UnityYamlWriter.WritesNew(o))
                        throw new NotSupportedException($"Unbound does not write {o.GetType().Name} objects added to a graph yet ({info.Path}).");
                    // An object of another asset file moved into this one: unmeasured.
                    if (AssetDatabaseRuntime.TryGetLocation(o, out var otherGuid, out _) && otherGuid != guid)
                        throw new NotSupportedException($"Unbound does not move an object of another asset into a graph file yet ({info.Path}: {o.GetType().Name} '{o.name}').");
                    gained.Add(o);
                    ids[o] = ++largest;
                }
            var contents = new HashSet<Object>(resource.m_Contents, ReferenceEqualityComparer.Instance) { resource };
            var lost = new List<Object>();
            foreach (var o in inFile)
                if (!contents.Contains(o)) lost.Add(o);
            docs.RemoveAll(d => loaded.Objects.TryGetValue(d.FileId, out var o) && o is not null && lost.Contains(o));

            var locations = new AssetDatabaseRuntime.Locations();
            int unlocated = 0;
            foreach (var doc in docs)
            {
                if (!loaded.Objects.TryGetValue(doc.FileId, out var o) || o is null) continue;
                if (ReferenceEquals(o, resource)) { WriteResource(doc.Body, resource, info.Path); continue; }
                var old = doc.Body;
                if (!NativeDocuments.Write(doc, o, ids, locations, ref unlocated))
                    throw new NotSupportedException($"Unbound does not write {o.GetType().Name} objects of a graph file yet ({info.Path}).");
                KeepTypeNames(old, doc.Body, info.Path);
            }
            foreach (var o in gained)
            {
                var doc = UnityYamlText.Parse(UnityYamlWriter.Write(new[] { o }, ids))[0];
                MonoTypeNames(doc.Body, 1, info.Path);
                docs.Add(doc);
            }
            if (unlocated > 0)
                Debug.LogWarning($"Unbound: {info.Path} is written with {unlocated} references to assets Unbound cannot name written as none.");
            // The Editor writes a file's objects in file id order (a new graph's skeleton holds its
            // graph before its resource).
            docs.Sort((a, b) => a.FileId.CompareTo(b.FileId));
            var written = UnityYamlText.Write(docs);
            // The same text again leaves the file as it is (and unimported).
            if (!AssetFiles.Unchanged(text, written)) SceneFiles.WriteFile(info.File, written);
            // The objects now in the file, and those no longer in it.
            foreach (var o in gained)
            {
                AssetDatabaseRuntime.AddObject(loaded, o, ids[o]);
                loaded.SourceObjects?.Add(o);
            }
            foreach (var o in lost)
            {
                AssetDatabaseRuntime.RemoveObject(loaded, o, ids[o]);
                loaded.SourceObjects?.Remove(o);
            }
            EditorUtility.ClearDirty(resource);
            foreach (var o in resource.m_Contents)
                if (o is not null) EditorUtility.ClearDirty(o);
        }

        /// <summary>The resource's own fields: its graph and its settings.</summary>
        static void WriteResource(YText body, VisualEffectResource resource, string path)
        {
            var infos = body.Get("m_Infos");
            if (infos == null || infos.Kind != YKind.Map)
                throw new NotSupportedException($"Unbound does not write a graph file whose resource has no settings map yet ({path}).");
            var s = resource.m_Settings;
            var renderer = infos.Get("m_RendererSettings");
            if (renderer != null && renderer.Kind == YKind.Map)
            {
                SetInt(renderer, "motionVectorGenerationMode", (int)s.Renderer.motionVectorGenerationMode);
                SetInt(renderer, "shadowCastingMode", (int)s.Renderer.shadowCastingMode);
                SetInt(renderer, "rayTracingMode", (int)s.Renderer.rayTracingMode);
                SetInt(renderer, "receiveShadows", s.Renderer.receiveShadows ? 1 : 0);
                SetInt(renderer, "reflectionProbeUsage", (int)s.Renderer.reflectionProbeUsage);
                SetInt(renderer, "lightProbeUsage", (int)s.Renderer.lightProbeUsage);
            }
            SetInt(infos, "m_CullingFlags", (int)s.CullingFlags);
            SetInt(infos, "m_UpdateMode", (int)s.UpdateMode);
            infos.Set("m_PreWarmDeltaTime", YText.Scalar(FloatText.Format(s.PreWarmDeltaTime)));
            infos.Set("m_PreWarmStepCount", YText.Scalar(s.PreWarmStepCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            // At two levels (under m_Infos): the value starts after its key.
            const string key = "m_InitialEventName";
            infos.Set(key, YText.Scalar(UnityYamlWriter.Scalar(s.InitialEventName ?? "", 4 + key.Length + 2, 6)));
            SetInt(infos, "m_InstancingMode", (int)s.InstancingMode);
            infos.Set("m_InstancingCapacity", YText.Scalar(s.InstancingCapacity.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        /// <summary>The graph's models keep types by name (the package's SerializableType writes a
        /// type's AssemblyQualifiedName). Unbound runs on .NET, whose class library names its types
        /// differently from the Editor's Mono (System.Single, mscorlib, Version=4.0.0.0 there), and
        /// the Editor's Mono cannot find .NET's names. A written name that stands for the same type
        /// as the file's keeps the file's text; a new .NET class-library name is refused.</summary>
        internal static void KeepTypeNames(YText old, YText written, string path, int level = 1)
        {
            if (written == null) return;
            if (old == null || old.Kind != written.Kind)
            {
                // A part the file did not hold: written as a new object's.
                MonoTypeNames(written, level, path);
                return;
            }
            if (written.Kind == YKind.Map)
            {
                for (int i = 0; i < written.Keys.Count; i++)
                {
                    var o = old.Get(written.Keys[i]);
                    var w = written.Values[i];
                    if (written.Keys[i] == "m_SerializableType" && w.Kind == YKind.Scalar && o != null && o.Kind == YKind.Scalar)
                    {
                        if (o.Raw == w.Raw) continue;
                        var oldType = Type.GetType(Unfold(o.Raw));
                        if (oldType != null && oldType == Type.GetType(Unfold(w.Raw))) written.Values[i] = o;
                        else Translate(written, i, level, path);
                    }
                    else if (written.Keys[i] == "m_SerializableType") Translate(written, i, level, path);
                    else KeepTypeNames(o, w, path, level + 1);
                }
            }
            else if (written.Kind == YKind.Seq)
                for (int i = 0; i < written.Items.Count; i++)
                    KeepTypeNames(i < old.Items.Count ? old.Items[i] : null, written.Items[i], path, level);
        }

        /// <summary>A new object's type names in the Editor's Mono form (<see cref="Translate"/>).
        /// <paramref name="level"/>: the indentation level of <paramref name="node"/>'s keys (a
        /// document body's are at 1; a sequence's item maps at its key's level plus one).</summary>
        internal static void MonoTypeNames(YText node, int level, string path)
        {
            if (node == null) return;
            if (node.Kind == YKind.Seq)
            {
                foreach (var item in node.Items) MonoTypeNames(item, level, path);
                return;
            }
            if (node.Kind != YKind.Map) return;
            for (int i = 0; i < node.Keys.Count; i++)
            {
                if (node.Keys[i] == "m_SerializableType") Translate(node, i, level, path);
                else MonoTypeNames(node.Values[i], level + 1, path);
            }
        }

        /// <summary>A type name the graph's models write, made the Editor's Mono form: a .NET class
        /// library type Mono keeps in mscorlib (the primitive types and string) named as Mono names
        /// it, laid out as the writer lays out a string at that level; another class library name is
        /// refused (the Editor's Mono could not read it back); other names are left as written.</summary>
        static void Translate(YText map, int i, int level, string path)
        {
            var value = map.Values[i];
            if (value.Kind != YKind.Scalar) return;
            var name = Unfold(value.Raw);
            const string core = ", System.Private.CoreLib, ";
            int at = name.IndexOf(core, StringComparison.Ordinal);
            if (at < 0) return;
            var type = name.Substring(0, at);
            if (type.Contains('`') || !ManagedReferenceIds.s_MonoCoreLibrary.Contains(type))
                throw new NotSupportedException($"Unbound does not write .NET class library type names into graph files yet ({path}: {name}).");
            var mono = type + ", " + ManagedReferenceIds.MonoCoreLibraryIdentity;
            map.Values[i] = YText.Scalar(UnityYamlWriter.Scalar(mono, level * 2 + map.Keys[i].Length + 2, (level + 1) * 2));
        }

        /// <summary>A plain scalar's text as one line (a long one is folded over lines in the file).</summary>
        static string Unfold(string raw)
        {
            var lines = raw.Split('\n');
            for (int i = 0; i < lines.Length; i++) lines[i] = lines[i].Trim();
            return string.Join(" ", lines).Trim();
        }

        static void SetInt(YText map, string key, int value) =>
            map.Set(key, YText.Scalar(value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }
}

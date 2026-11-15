using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>Matches private input trees against declarations generated from built-in readers,
    /// and the script reader's member table. No runtime objects or project scripts are created.</summary>
    internal static class FieldCoverage
    {
        internal sealed class Input
        {
            public string Context { get; set; }
            public int ClassId { get; set; }
            public long FileId { get; set; }
            public string FileGuid { get; set; }
            public string TypeName { get; set; }
            public JsonElement Body { get; set; }
        }

        internal sealed class Result
        {
            public long FileId { get; set; }
            public List<string> Consumed { get; set; } = new List<string>();
            public string Error { get; set; }
            public string Dependency { get; set; }
            public LightingInput Lighting { get; set; }
        }

        internal sealed class LightingInput
        {
            public bool? RealtimeGI { get; set; }
            public string DataGuid { get; set; }
            public long DataId { get; set; }
            public string SettingsGuid { get; set; }
            public long SettingsId { get; set; }
        }

        sealed class References : IReferenceResolver
        {
            public Object Resolve(ObjectRef reference, Type expected) => null;
        }

        /// <summary>The scene and bundle documents with fixed readers (each a case of
        /// <see cref="Declarations"/>), not read by their registered type.</summary>
        internal static readonly int[] FixedDocumentIds =
            { EngineTypes.PrefabInstanceId, 1660057539, 142, 115, 150, EngineTypes.GameObjectId, 104, 157, 196, 124 };

        /// <summary>The project settings with readers (cases of <see cref="Declarations"/>).</summary>
        internal static readonly int[] ProjectSettingsIds = { 55, 19, 126 };

        static bool IsFixed(int classId) => Array.IndexOf(FixedDocumentIds, classId) >= 0;

        /// <summary>The reader declarations whose generated field tables a document of this kind
        /// consumes (the readers the loader runs for it); <paramref name="type"/> is the script type of
        /// a MonoBehaviour, else the registered engine type. Shared with the managed tests, which
        /// require every key they name to be derived.</summary>
        internal static IEnumerable<(Type Owner, string Method, int Parameters, int Node)> Declarations(string context, int classId, Type type)
        {
            if (context == "projectSettings")
            {
                if (classId == 55) yield return (typeof(Physics), nameof(Physics.ReadSettings), 1, 0);
                if (classId == 19) yield return (typeof(Physics2D), nameof(Physics2D.ReadSettings), 1, 0);
                if (classId == 126) yield return (typeof(NavMeshAreas), nameof(NavMeshAreas.Read), 1, 0);
                yield break;
            }
            if (context != null) yield break;
            switch (classId)
            {
                case EngineTypes.MonoBehaviourId:
                    yield return (typeof(ObjectGraphBuilder), nameof(ObjectGraphBuilder.OwnerOf), 1, 0);
                    // Without its script the rest is not read (the report names the missing script).
                    if (type == null) yield break;
                    if (typeof(Component).IsAssignableFrom(type)) yield return (typeof(ObjectGraphBuilder), nameof(ObjectGraphBuilder.ReadComponentHeader), 2, 0);
                    else yield return (typeof(ObjectGraphBuilder), nameof(ObjectGraphBuilder.ReadName), 2, 1);
                    if (typeof(Behaviour).IsAssignableFrom(type)) yield return (typeof(ObjectGraphBuilder), nameof(ObjectGraphBuilder.ReadBehaviour), 2, 1);
                    yield break;
                case EngineTypes.PrefabInstanceId: yield return (typeof(PrefabLinks), nameof(PrefabLinks.ReadInstance), 2, 0); yield break;
                case 1660057539: yield return (typeof(ObjectGraphBuilder), nameof(ObjectGraphBuilder.RootReferences), 1, 0); yield break;
                case 142: yield return (typeof(AssetBundle), nameof(AssetBundle.ReadContentsDocument), 1, 0); yield break;
                case 115: yield return (typeof(BundleFiles), nameof(BundleFiles.ScriptType), 1, 0); yield break;
                // Recognized by the loader, but its data has no reader.
                case 150: yield break;
                case EngineTypes.GameObjectId:
                    yield return (typeof(ObjectGraphBuilder), nameof(ObjectGraphBuilder.ReadGameObject), 4, 0);
                    yield return (typeof(ObjectGraphBuilder), nameof(ObjectGraphBuilder.ComponentReferences), 1, 0);
                    yield break;
                case 104: yield return (typeof(RenderSettings), nameof(RenderSettings.SceneLoaded), 3, 1); yield break;
                case 157: yield return (typeof(SceneSettings), nameof(SceneSettings.LightingReferences), 1, 0); yield break;
                case 196: yield return (typeof(SceneNavMesh), nameof(SceneNavMesh.SceneLoaded), 3, 1); yield break;
                case 124:
                    yield return (typeof(ObjectGraphBuilder), nameof(ObjectGraphBuilder.ReadComponentHeader), 2, 0);
                    yield return (typeof(ObjectGraphBuilder), nameof(ObjectGraphBuilder.ReadBehaviour), 2, 1);
                    yield break;
            }
            if (type == null) yield break;
            yield return (typeof(ObjectGraphBuilder), nameof(ObjectGraphBuilder.OwnerOf), 1, 0);
            if (typeof(Component).IsAssignableFrom(type))
            {
                yield return (typeof(ObjectGraphBuilder), nameof(ObjectGraphBuilder.ReadComponentHeader), 2, 0);
                if (typeof(Behaviour).IsAssignableFrom(type)) yield return (typeof(ObjectGraphBuilder), nameof(ObjectGraphBuilder.ReadBehaviour), 2, 1);
                if (typeof(Transform).IsAssignableFrom(type))
                {
                    yield return (typeof(ObjectGraphBuilder), nameof(ObjectGraphBuilder.ParentOf), 1, 0);
                    yield return (typeof(ObjectGraphBuilder), nameof(ObjectGraphBuilder.RootOrder), 1, 0);
                    yield return (typeof(ObjectGraphBuilder), nameof(ObjectGraphBuilder.ChildReferences), 1, 0);
                }
                var method = type.GetMethod("ReadSerialized", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                yield return (method.DeclaringType, method.Name, method.GetParameters().Length, 0);
            }
            else
            {
                yield return (typeof(ObjectGraphBuilder), nameof(ObjectGraphBuilder.ReadName), 2, 1);
                if (!typeof(ScriptableObject).IsAssignableFrom(type) && AssetAdapters.Reader(type) is System.Reflection.MethodInfo method)
                    yield return (method.DeclaringType, method.Name, method.GetParameters().Length, 0);
            }
        }

        internal static List<Result> Report(List<Input> documents)
        {
            var results = new List<Result>();
            var scripts = new Dictionary<(string File, long Id), Type>();
            var cameras = new Dictionary<(string File, long Owner), bool>();
            var owners = new Dictionary<(string File, long Id), bool>();
            if (documents.Any(input => input.Context == null && input.ClassId == 124))
            {
                foreach (var input in documents.Where(input => input.Context == null && input.ClassId == 1))
                    owners[(input.FileGuid ?? "", input.FileId)] = !Property(input.Body, "m_CorrespondingSourceObject").AsRef().IsNull;
                foreach (var input in documents.Where(input => input.Context == null && input.ClassId == 20))
                    cameras[(input.FileGuid ?? "", Property(input.Body, "m_GameObject").AsRef().FileId)] = Property(input.Body, "m_Enabled").AsBool(true);
            }
            foreach (var group in documents.Where(doc => doc.ClassId == 115 && doc.FileGuid?.StartsWith("bundle:", StringComparison.Ordinal) == true).GroupBy(doc => doc.FileGuid))
                BundleFiles.RegisterScripts(group.Key.Substring("bundle:".Length), group.Select(input => new YDocument
                {
                    ClassId = input.ClassId, FileId = input.FileId, Body = Node(input.Body),
                }).ToList(), scripts);
            foreach (var doc in documents)
            {
                var result = new Result { FileId = doc.FileId };
                results.Add(result);
                var body = Node(doc.Body);
                Track(body);
                void Schema(Type owner, string method, int parameters, int nodeParameter = 0) =>
                    ReaderFieldSchema.Consume(body, ReaderFieldSchema.Fields(owner, method, parameters, nodeParameter));
                try
                {
                    if (doc.Context == null && doc.ClassId == 850595691)
                        result.Lighting = new LightingInput { RealtimeGI = LightingSettings.ReadRealtimeGI(body) };
                    Type scriptType = null;
                    if (doc.Context == null && doc.ClassId == EngineTypes.MonoBehaviourId)
                        scriptType = ObjectGraphBuilder.ScriptOf(body.Ref("m_Script"), doc.FileGuid, scripts);
                    var type = doc.Context != null ? null : doc.ClassId == EngineTypes.MonoBehaviourId ? scriptType
                        : EngineTypes.ForClassId(doc.ClassId, doc.TypeName, warn: false);
                    if (doc.Context == null && doc.ClassId == 157)
                    {
                        var lighting = SceneSettings.LightingReferences(body);
                        result.Lighting = new LightingInput { DataGuid = lighting.Data.Guid, DataId = lighting.Data.FileId,
                            SettingsGuid = lighting.Settings.Guid, SettingsId = lighting.Settings.FileId };
                    }
                    foreach (var (owner, method, parameters, node) in Declarations(doc.Context, doc.ClassId, type))
                        Schema(owner, method, parameters, node);
                    if (doc.Context == null) switch (doc.ClassId)
                    {
                        case EngineTypes.MonoBehaviourId:
                            if (scriptType == null) throw new NotSupportedException("script type is missing or its assembly failed to compile");
                            new YamlReader(new References()).ScanScriptObject(scriptType, body);
                            break;
                        case 124:
                            var owner = body.Ref("m_GameObject").FileId;
                            if (!cameras.TryGetValue((doc.FileGuid ?? "", owner), out bool cameraEnabled))
                            {
                                if (!owners.TryGetValue((doc.FileGuid ?? "", owner), out bool inherited) || inherited)
                                    result.Dependency = "Camera dependency unresolved: owner or inherited components require prefab expansion";
                                else result.Dependency = Compat.FlareCameraRepairMeasured
                                    ? "Camera missing: loader creates Camera and warns, as in the Unity 6000.0.71f1 Editor"
                                    : "Camera missing: load repair is unmeasured for this release and the loader rejects it";
                            }
                            else if (!cameraEnabled)
                                result.Dependency = "Camera disabled: retained disabled; no automatic LensFlare drawing, explicit Camera.Render remains available";
                            break;
                        default:
                            if (IsFixed(doc.ClassId)) break;
                            if (type == null) throw new NotSupportedException("no registered serialized type");
                            if (!typeof(Component).IsAssignableFrom(type) && typeof(ScriptableObject).IsAssignableFrom(type))
                                new YamlReader(new References()).ScanScriptObject(type, body);
                            break;
                    }
                }
                catch (Exception e) { result.Error = e.GetType().Name + ": " + e.Message; }
                Leaves(body, "", result.Consumed);
                result.Consumed.Sort(StringComparer.Ordinal);
            }
            return results;
        }

        internal static void Track(YNode node)
        {
            if (ReferenceEquals(node, YNode.Null)) return;
            node.CoverageOn = true;
            node.CoverageRead = false;
            foreach (var child in node.Values ?? node.Items ?? Array.Empty<YNode>()) Track(child);
        }

        internal static void Leaves(YNode node, string path, List<string> consumed, bool parentRead = false)
        {
            if (node.Kind == YKind.Map && node.Keys.Length != 0)
            {
                for (int i = 0; i < node.Keys.Length; i++)
                    Leaves(node.Values[i], JoinPath(path, node.Keys[i]), consumed);
            }
            else if (node.Kind == YKind.Seq && node.Items.Length != 0)
            {
                foreach (var item in node.Items) Leaves(item, path + "[]", consumed, node.CoverageRead);
            }
            else if (node.CoverageRead || parentRead) consumed.Add(path);
        }

        static string JoinPath(string prefix, string key)
        {
            key = key.Replace("\\", "\\\\").Replace(".", "\\.").Replace("[", "\\[").Replace("]", "\\]");
            return prefix.Length == 0 ? key : prefix + "." + key;
        }

        static YNode Property(JsonElement body, string name) =>
            body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out var value) ? Node(value) : YNode.Null;

        static YNode Node(JsonElement value) => YNode.FromJson(value);
    }
}

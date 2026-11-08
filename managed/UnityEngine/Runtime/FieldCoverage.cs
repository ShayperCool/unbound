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
        }

        sealed class References : IReferenceResolver
        {
            public Object Resolve(ObjectRef reference, Type expected) => null;
        }

        internal static List<Result> Report(List<Input> documents)
        {
            var results = new List<Result>();
            var scripts = new Dictionary<(string File, long Id), Type>();
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
                    switch (doc.ClassId)
                    {
                        case EngineTypes.MonoBehaviourId:
                            Schema(typeof(ObjectGraphBuilder), nameof(ObjectGraphBuilder.OwnerOf), 1);
                            var script = body.Ref("m_Script");
                            var scriptType = ObjectGraphBuilder.ScriptOf(script, doc.FileGuid, scripts);
                            if (scriptType == null) throw new NotSupportedException("script type is missing or its assembly failed to compile");
                            if (typeof(Component).IsAssignableFrom(scriptType)) Schema(typeof(ObjectGraphBuilder), nameof(ObjectGraphBuilder.ReadComponentHeader), 2);
                            else Schema(typeof(ObjectGraphBuilder), nameof(ObjectGraphBuilder.ReadName), 2, 1);
                            if (typeof(Behaviour).IsAssignableFrom(scriptType)) Schema(typeof(ObjectGraphBuilder), nameof(ObjectGraphBuilder.ReadBehaviour), 2, 1);
                            new YamlReader(new References()).ScanScriptObject(scriptType, body);
                            break;
                        case EngineTypes.PrefabInstanceId:
                            Schema(typeof(PrefabLinks), nameof(PrefabLinks.ReadInstance), 2);
                            break;
                        case 1660057539:
                            Schema(typeof(ObjectGraphBuilder), nameof(ObjectGraphBuilder.RootReferences), 1);
                            break;
                        case 142:
                            Schema(typeof(AssetBundle), nameof(AssetBundle.ReadContentsDocument), 1);
                            break;
                        case 115:
                            Schema(typeof(BundleFiles), nameof(BundleFiles.ScriptType), 1);
                            break;
                        case 150:
                            // Recognized by the loader, but its data has no reader.
                            break;
                        case EngineTypes.GameObjectId:
                            Schema(typeof(ObjectGraphBuilder), nameof(ObjectGraphBuilder.ReadGameObject), 4);
                            Schema(typeof(ObjectGraphBuilder), nameof(ObjectGraphBuilder.ComponentReferences), 1);
                            break;
                        case 104:
                            Schema(typeof(RenderSettings), nameof(RenderSettings.SceneLoaded), 3, 1);
                            break;
                        case 157:
                            Schema(typeof(SceneSettings), nameof(SceneSettings.LightingReferences), 1);
                            break;
                        case 196:
                            Schema(typeof(SceneNavMesh), nameof(SceneNavMesh.SceneLoaded), 3, 1);
                            break;
                        default:
                            var type = EngineTypes.ForClassId(doc.ClassId, doc.TypeName, warn: false);
                            if (type == null) throw new NotSupportedException("no registered serialized type");
                            Schema(typeof(ObjectGraphBuilder), nameof(ObjectGraphBuilder.OwnerOf), 1);
                            if (typeof(Component).IsAssignableFrom(type))
                            {
                                Schema(typeof(ObjectGraphBuilder), nameof(ObjectGraphBuilder.ReadComponentHeader), 2);
                                if (typeof(Behaviour).IsAssignableFrom(type)) Schema(typeof(ObjectGraphBuilder), nameof(ObjectGraphBuilder.ReadBehaviour), 2, 1);
                                if (typeof(Transform).IsAssignableFrom(type))
                                {
                                    Schema(typeof(ObjectGraphBuilder), nameof(ObjectGraphBuilder.ParentOf), 1);
                                    Schema(typeof(ObjectGraphBuilder), nameof(ObjectGraphBuilder.RootOrder), 1);
                                    Schema(typeof(ObjectGraphBuilder), nameof(ObjectGraphBuilder.ChildReferences), 1);
                                }
                                var method = type.GetMethod("ReadSerialized", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                                Schema(method.DeclaringType, method.Name, method.GetParameters().Length);
                            }
                            else
                            {
                                Schema(typeof(ObjectGraphBuilder), nameof(ObjectGraphBuilder.ReadName), 2, 1);
                                if (typeof(ScriptableObject).IsAssignableFrom(type)) new YamlReader(new References()).ScanScriptObject(type, body);
                                else if (typeof(ISerializedAsset).IsAssignableFrom(type))
                                {
                                    var map = type.GetInterfaceMap(typeof(ISerializedAsset));
                                    var method = map.TargetMethods[0];
                                    Schema(method.DeclaringType, method.Name, method.GetParameters().Length);
                                }
                            }
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

        static YNode Node(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    var keys = new List<string>();
                    var values = new List<YNode>();
                    foreach (var p in value.EnumerateObject()) { keys.Add(p.Name); values.Add(Node(p.Value)); }
                    return new YNode { Kind = YKind.Map, Keys = keys.ToArray(), Values = values.ToArray() };
                case JsonValueKind.Array:
                    var items = new List<YNode>();
                    foreach (var item in value.EnumerateArray()) items.Add(Node(item));
                    return new YNode { Kind = YKind.Seq, Items = items.ToArray() };
                case JsonValueKind.Null: return new YNode { Kind = YKind.Null };
                default: return YNode.MakeScalar(value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString());
            }
        }
    }
}

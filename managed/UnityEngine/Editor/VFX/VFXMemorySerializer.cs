using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Unbound.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor.VFX
{
    /// <summary>
    /// In-memory snapshots of a graph's models, which the graph package takes around a compile
    /// (to put the graph back as it was) and to copy subgraphs. A snapshot keeps each object's
    /// serialized fields with references by instance: restored into the same objects, or into
    /// new objects whose references to one another follow the copies. The bytes are Unbound's own
    /// (nothing reads them but <see cref="ExtractObjects(byte[], bool)"/> in the same session).
    /// </summary>
    internal static partial class VFXMemorySerializer
    {
        sealed class Entry
        {
            public int Id { get; set; }
            public string Type { get; set; }
            public string Name { get; set; }
            public int HideFlags { get; set; }
            public string Json { get; set; }
        }

        public static string StoreObjects(ScriptableObject[] objects) => JsonSerializer.Serialize(Capture(objects));

        public static byte[] StoreObjectsToByteArray(ScriptableObject[] objects, CompressionLevel compressionLevel = CompressionLevel.None) =>
            Encoding.UTF8.GetBytes(StoreObjects(objects));

        public static ScriptableObject[] ExtractObjects(string data, bool asACopy) =>
            Restore(string.IsNullOrEmpty(data) ? new List<Entry>() : JsonSerializer.Deserialize<List<Entry>>(data), asACopy);

        public static ScriptableObject[] ExtractObjects(byte[] data, bool asACopy) =>
            ExtractObjects(data == null ? null : Encoding.UTF8.GetString(data), asACopy);

        public static ScriptableObject[] DuplicateObjects(ScriptableObject[] objects) => Restore(Capture(objects), true);

        static List<Entry> Capture(ScriptableObject[] objects)
        {
            var entries = new List<Entry>();
            if (objects == null) return entries;
            foreach (var o in objects)
            {
                if (o == null) continue;
                entries.Add(new Entry
                {
                    Id = o.GetInstanceID(),
                    Type = o.GetType().AssemblyQualifiedName,
                    Name = o.m_Name,
                    HideFlags = (int)o.hideFlags,
                    Json = JsonUtility.ToJson(o),
                });
            }
            return entries;
        }

        static ScriptableObject[] Restore(List<Entry> entries, bool asACopy)
        {
            var result = new ScriptableObject[entries.Count];
            if (!asACopy)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    result[i] = ObjectRegistry.FindById(entries[i].Id) as ScriptableObject;
                    if (result[i] == null) continue;
                    result[i].m_Name = entries[i].Name;
                    result[i].hideFlags = (HideFlags)entries[i].HideFlags;
                    JsonUtility.FromJsonOverwrite(entries[i].Json, result[i]);
                }
                return result;
            }
            // Copies: every object exists before any is read, so that references among the
            // snapshot's objects find the copies; references to other objects stay as they are.
            var copies = new Dictionary<int, int>();
            for (int i = 0; i < entries.Count; i++)
            {
                var type = Type.GetType(entries[i].Type) ?? GameDomain.FindTypeByName(entries[i].Type.Split(',')[0]);
                if (type == null || !typeof(ScriptableObject).IsAssignableFrom(type)) continue;
                result[i] = ScriptableObject.CreateInstanceWithoutCallbacks(type);
                if (result[i] == null) continue;
                result[i].m_Name = entries[i].Name;
                result[i].hideFlags = (HideFlags)entries[i].HideFlags;
                copies[entries[i].Id] = result[i].GetInstanceID();
            }
            for (int i = 0; i < entries.Count; i++)
                if (result[i] != null) JsonUtility.FromJsonOverwrite(RemapReferences(entries[i].Json, copies), result[i]);
            foreach (var copy in result)
                if (copy != null) copy.RunCreationCallbacks();
            return result;
        }

        const string Reference = "\"instanceID\":";

        /// <summary>The JSON with each object reference to a copied object pointing at its copy.</summary>
        static string RemapReferences(string json, Dictionary<int, int> copies)
        {
            if (copies.Count == 0 || json.IndexOf(Reference, StringComparison.Ordinal) < 0) return json;
            var text = new StringBuilder(json.Length);
            int at = 0;
            while (true)
            {
                int found = json.IndexOf(Reference, at, StringComparison.Ordinal);
                if (found < 0) break;
                int start = found + Reference.Length, end = start;
                while (end < json.Length && (json[end] == '-' || char.IsDigit(json[end]))) end++;
                text.Append(json, at, start - at);
                if (int.TryParse(json.AsSpan(start, end - start), out int id) && copies.TryGetValue(id, out int copy)) text.Append(copy);
                else text.Append(json, start, end - start);
                at = end;
            }
            text.Append(json, at, json.Length - at);
            return text.ToString();
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Content;
using UnityEngine;

// The build pipeline's ObjectIdentifier as serialized data: a serializable struct whose fields are
// internal, so it serializes as an empty object at the root and inside a serialized class, JSON
// overwrite leaves it unchanged, and an Instantiate'd ScriptableObject keeps the field's initializer
// value instead of the source's. Controls beside it (an int, a struct with a public field) are copied.
public class ObjectIdentifierProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("ObjectIdentifierProbe").AddComponent<ObjectIdentifierProbe>(); }
    [Serializable] class Entry { public string key; public string value; }
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly List<(string, string)> rows = new List<(string, string)>();
    static void Put(string key, object value) => rows.Add((key, Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)));

    [Serializable] public struct Plain { public int value; }
    [Serializable] public class Holder { public ObjectIdentifier identifier = Make(29, "initializer.asset"); public Plain plain; public int control = 19; }
    public class Asset : ScriptableObject { public ObjectIdentifier identifier = Make(29, "initializer.asset"); public Plain plain; public int control = 19; }

    static ObjectIdentifier Make(long fileId, string path)
    {
        object boxed = default(ObjectIdentifier);
        var type = boxed.GetType();
        type.GetField("m_GUID", All).SetValue(boxed, new GUID("0123456789abcdef0123456789abcdef"));
        type.GetField("m_LocalIdentifierInFile", All).SetValue(boxed, fileId);
        type.GetField("m_FileType", All).SetValue(boxed, FileType.SerializedAssetType);
        type.GetField("m_FilePath", All).SetValue(boxed, path);
        return (ObjectIdentifier)boxed;
    }

    static string Id(ObjectIdentifier id) => id.localIdentifierInFile + " " + id.filePath;

    void Start()
    {
        string path = Environment.GetEnvironmentVariable("OBJECT_IDENTIFIER_OUT"); if (string.IsNullOrEmpty(path)) return;
        bool failed = false;
        try { Record(); }
        catch (Exception ex) { Debug.LogException(ex); failed = true; }
        using (var output = new StreamWriter(path))
        {
            foreach (var (key, value) in rows) output.WriteLine(JsonUtility.ToJson(new Entry { key = key, value = value }));
            if (!failed) output.WriteLine(JsonUtility.ToJson(new Entry { key = "complete", value = "1" }));
        }
        EditorApplication.Exit(failed ? 1 : 0);
    }

    void Record()
    {
        Put("serializable", typeof(ObjectIdentifier).IsSerializable);
        Put("json.root", JsonUtility.ToJson(Make(17, "source.asset")));
        var holder = new Holder { identifier = Make(17, "source.asset"), plain = new Plain { value = 5 }, control = 23 };
        Put("json.nested", JsonUtility.ToJson(holder));
        var target = new Holder();
        JsonUtility.FromJsonOverwrite("{\"identifier\":{\"m_LocalIdentifierInFile\":41,\"m_FilePath\":\"json.asset\"},\"plain\":{\"value\":7},\"control\":31}", target);
        Put("json.overwrite", Id(target.identifier) + " plain " + target.plain.value + " control " + target.control);
        var boxedRoot = (object)Make(17, "source.asset");
        JsonUtility.FromJsonOverwrite("{\"m_LocalIdentifierInFile\":43}", boxedRoot);
        Put("json.overwriteRoot", Id((ObjectIdentifier)boxedRoot));
        var restored = JsonUtility.FromJson<Holder>(JsonUtility.ToJson(holder));
        Put("json.roundTrip", Id(restored.identifier) + " plain " + restored.plain.value + " control " + restored.control);

        var original = ScriptableObject.CreateInstance<Asset>();
        original.identifier = Make(17, "source.asset");
        original.plain = new Plain { value = 5 };
        original.control = 23;
        var clone = UnityEngine.Object.Instantiate(original);
        Put("clone", Id(clone.identifier) + " plain " + clone.plain.value + " control " + clone.control);
        UnityEngine.Object.DestroyImmediate(clone);
        UnityEngine.Object.DestroyImmediate(original);
    }
}
